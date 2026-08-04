using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public enum TipcardActivationStatus
{
    Ready,
    NotFound,
    LightningUnavailable,
    PullPaymentUnavailable,
    Failed
}

public sealed record TipcardActivationResult(
    TipcardActivationStatus Status,
    StoreData Store,
    TipcardSetData Set,
    TipcardData Card,
    PullPaymentData PullPayment,
    bool LightningConfigured);

public class TipcardService
{
    public const string SettingsKey = "TipcardsSettings";

    private static readonly PayoutMethodId LightningPayoutMethod = PayoutMethodId.Parse("BTC-LN");
    private static readonly PayoutMethodId[] CardPayoutMethods =
    [
        PayoutMethodId.Parse("BTC-CHAIN"),
        LightningPayoutMethod
    ];

    private readonly StoreRepository _storeRepository;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly TipcardStoreLock _storeLock;
    private readonly ILogger<TipcardService> _logger;

    public TipcardService(
        StoreRepository storeRepository,
        PullPaymentHostedService pullPaymentHostedService,
        ApplicationDbContextFactory dbContextFactory,
        PayoutMethodHandlerDictionary payoutHandlers,
        TipcardStoreLock storeLock,
        ILogger<TipcardService> logger)
    {
        _storeRepository = storeRepository;
        _pullPaymentHostedService = pullPaymentHostedService;
        _dbContextFactory = dbContextFactory;
        _payoutHandlers = payoutHandlers;
        _storeLock = storeLock;
        _logger = logger;
    }

    public static List<TipcardData> CreateCards(int count, int firstCardNumber = 1)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        return Enumerable.Range(firstCardNumber, count)
            .Select(cardNumber => new TipcardData
            {
                ClaimId = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
                CardNumber = cardNumber
            })
            .ToList();
    }

    public virtual async Task<TipcardsStoreSettings> GetSettingsAsync(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<TipcardsStoreSettings>(storeId, SettingsKey)
                       ?? new TipcardsStoreSettings();
        settings.Sets ??= new List<TipcardSetData>();
        settings.WalletRecommendations ??= TipcardsStoreSettings.DefaultWalletRecommendations;
        foreach (var set in settings.Sets)
            set.Cards ??= new List<TipcardData>();
        return settings;
    }

    public virtual Task SaveSettingsAsync(string storeId, TipcardsStoreSettings settings)
    {
        return _storeRepository.UpdateSetting(storeId, SettingsKey, settings);
    }

    public virtual bool HasLightningPayouts(StoreData store)
    {
        return store != null && _payoutHandlers.GetSupportedPayoutMethods(store).Contains(LightningPayoutMethod);
    }

    public virtual bool SupportsLnurl(PullPaymentData pullPayment)
    {
        return pullPayment != null && _pullPaymentHostedService.SupportsLNURL(pullPayment, pullPayment.GetBlob());
    }

    public virtual async Task<Dictionary<string, PullPaymentData>> GetPullPaymentsAsync(
        IEnumerable<TipcardData> cards)
    {
        var pullPaymentIds = cards
            .Select(card => card.PullPaymentId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToArray();

        if (pullPaymentIds.Length == 0)
            return new Dictionary<string, PullPaymentData>();

        await using var ctx = _dbContextFactory.CreateContext();
        return await ctx.PullPayments
            .Include(payment => payment.Payouts)
            .Where(payment => pullPaymentIds.Contains(payment.Id))
            .ToDictionaryAsync(payment => payment.Id);
    }

    public virtual async Task CancelPullPaymentsAsync(string storeId, IEnumerable<string> pullPaymentIds)
    {
        var ids = pullPaymentIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return;

        await _pullPaymentHostedService.Cancel(new PullPaymentHostedService.CancelRequest(ids)
        {
            StoreIds = [storeId]
        });
    }

    public async Task<TipcardActivationResult> EnsurePullPaymentAsync(
        string storeId,
        string claimId,
        CancellationToken cancellationToken = default)
    {
        var initialSettings = await GetSettingsAsync(storeId);
        if (!TryFindCard(initialSettings, claimId, out _, out _))
            return new TipcardActivationResult(TipcardActivationStatus.NotFound, null, null, null, null, false);

        using var storeLease = await _storeLock.LockAsync(storeId, cancellationToken);

        var settings = await GetSettingsAsync(storeId);
        if (!TryFindCard(settings, claimId, out var set, out var card))
            return new TipcardActivationResult(TipcardActivationStatus.NotFound, null, null, null, null, false);

        var store = await FindStoreAsync(storeId);
        if (store == null)
            return new TipcardActivationResult(TipcardActivationStatus.NotFound, null, set, card, null, false);

        var lightningConfigured = HasLightningPayouts(store);

        if (!string.IsNullOrEmpty(card.PullPaymentId))
        {
            var pullPayment = await GetPullPaymentAsync(card.PullPaymentId);
            if (pullPayment == null || pullPayment.StoreId != storeId || !pullPayment.IsRunning())
            {
                return new TipcardActivationResult(
                    TipcardActivationStatus.PullPaymentUnavailable,
                    store,
                    set,
                    card,
                    pullPayment,
                    lightningConfigured);
            }

            return new TipcardActivationResult(
                TipcardActivationStatus.Ready,
                store,
                set,
                card,
                pullPayment,
                lightningConfigured);
        }

        if (!lightningConfigured)
        {
            return new TipcardActivationResult(
                TipcardActivationStatus.LightningUnavailable,
                store,
                set,
                card,
                null,
                false);
        }

        string createdPullPaymentId = null;
        try
        {
            createdPullPaymentId = await CreatePullPaymentAsync(store, set, card);
            var pullPayment = await GetPullPaymentAsync(createdPullPaymentId);
            if (pullPayment == null)
                throw new InvalidOperationException("The newly created pull payment could not be loaded.");

            card.PullPaymentId = createdPullPaymentId;
            await SaveSettingsAsync(storeId, settings);

            return new TipcardActivationResult(
                TipcardActivationStatus.Ready,
                store,
                set,
                card,
                pullPayment,
                true);
        }
        catch (Exception ex)
        {
            card.PullPaymentId = null;
            if (!string.IsNullOrEmpty(createdPullPaymentId))
            {
                try
                {
                    await CancelPullPaymentsAsync(storeId, [createdPullPaymentId]);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException,
                        "Failed to archive pull payment {PullPaymentId} after Tipcard activation failed",
                        createdPullPaymentId);
                }
            }

            _logger.LogError(ex,
                "Failed to activate Tipcard {ClaimId} in store {StoreId}",
                claimId,
                storeId);
            return new TipcardActivationResult(
                TipcardActivationStatus.Failed,
                store,
                set,
                card,
                null,
                true);
        }
    }

    protected virtual Task<StoreData> FindStoreAsync(string storeId)
    {
        return _storeRepository.FindStore(storeId);
    }

    protected virtual async Task<PullPaymentData> GetPullPaymentAsync(string pullPaymentId)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        return await ctx.PullPayments
            .Include(payment => payment.Payouts)
            .FirstOrDefaultAsync(payment => payment.Id == pullPaymentId);
    }

    private static bool TryFindCard(
        TipcardsStoreSettings settings,
        string claimId,
        out TipcardSetData set,
        out TipcardData card)
    {
        set = settings.Sets.FirstOrDefault(candidate =>
            candidate.Cards.Any(candidateCard =>
                string.Equals(candidateCard.ClaimId, claimId, StringComparison.Ordinal)));
        card = set?.Cards.FirstOrDefault(candidate =>
            string.Equals(candidate.ClaimId, claimId, StringComparison.Ordinal));
        return set != null && card != null;
    }

    protected virtual async Task<string> CreatePullPaymentAsync(
        StoreData store,
        TipcardSetData set,
        TipcardData card)
    {
        var supportedPaymentMethods = _payoutHandlers.GetSupportedPayoutMethods(store);
        return await _pullPaymentHostedService.CreatePullPayment(store, new()
        {
            Amount = set.SatsPerCard / 100_000_000m,
            Currency = "BTC",
            Name = $"Tipcard {set.Name} #{card.CardNumber}",
            Description = $"tipcard-set:{set.Id}",
            PayoutMethods = CardPayoutMethods
                .Where(supportedPaymentMethods.Contains)
                .Select(method => method.ToString())
                .ToArray(),
            BOLT11Expiration = TimeSpan.FromDays(365),
            AutoApproveClaims = true
        });
    }
}
