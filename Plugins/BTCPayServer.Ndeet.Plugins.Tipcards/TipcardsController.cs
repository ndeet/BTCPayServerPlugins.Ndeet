using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Ndeet.Plugins.Tipcards.ViewModels;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public class TipcardsController : Controller
{
    private const string SettingsKey = "TipcardsSettings";

    private readonly StoreRepository _storeRepository;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly UriResolver _uriResolver;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly RateFetcher _rateFetcher;
    private readonly DefaultRulesCollection _defaultRulesCollection;

    public TipcardsController(
        StoreRepository storeRepository,
        PullPaymentHostedService pullPaymentHostedService,
        ApplicationDbContextFactory dbContextFactory,
        PayoutMethodHandlerDictionary payoutHandlers,
        UriResolver uriResolver,
        BTCPayNetworkProvider networkProvider,
        RateFetcher rateFetcher,
        DefaultRulesCollection defaultRulesCollection)
    {
        _storeRepository = storeRepository;
        _pullPaymentHostedService = pullPaymentHostedService;
        _dbContextFactory = dbContextFactory;
        _payoutHandlers = payoutHandlers;
        _uriResolver = uriResolver;
        _networkProvider = networkProvider;
        _rateFetcher = rateFetcher;
        _defaultRulesCollection = defaultRulesCollection;
    }

    private StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("~/plugins/{storeId}/tipcards")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListSets(string storeId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var vm = new ListTipcardSetsViewModel
        {
            LightningConfigured = HasLightningPayouts()
        };

        foreach (var set in settings.Sets.OrderByDescending(s => s.CreatedDate))
        {
            var claimedCount = await CountClaimedCards(set.PullPaymentIds);
            vm.Sets.Add(new TipcardSetViewModel
            {
                Id = set.Id,
                Name = set.Name,
                TotalCards = set.NumberOfCards,
                ClaimedCards = claimedCount,
                SatsPerCard = set.SatsPerCard,
                CreatedDate = set.CreatedDate
            });
        }

        return View(vm);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult CreateSet(string storeId)
    {
        if (CurrentStore == null)
            return NotFound();

        if (!HasLightningPayouts())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "You must enable Lightning payouts before creating tipcards.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(ListSets), new { storeId });
        }

        return View(new CreateTipcardSetViewModel());
    }

    [HttpPost("~/plugins/{storeId}/tipcards/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateSet(string storeId, CreateTipcardSetViewModel model)
    {
        if (CurrentStore == null)
            return NotFound();

        if (!ModelState.IsValid)
            return View(model);

        if (!HasLightningPayouts())
        {
            TempData[WellKnownTempData.ErrorMessage] = "You must enable Lightning payouts before creating tipcards.";
            return RedirectToAction(nameof(ListSets), new { storeId });
        }

        var setId = Encoders.Base58.EncodeData(RandomUtils.GetBytes(8));
        var amountInBtc = model.SatsPerCard / 100_000_000m;
        var pullPaymentIds = new List<string>();

        for (int i = 0; i < model.NumberOfCards; i++)
        {
            var ppId = await CreateCardPullPayment(setId, model.Name, i + 1, amountInBtc);
            pullPaymentIds.Add(ppId);
        }

        var settings = await GetSettings();
        settings.Sets.Add(new TipcardSetData
        {
            Id = setId,
            Name = model.Name,
            SatsPerCard = model.SatsPerCard,
            NumberOfCards = model.NumberOfCards,
            PullPaymentIds = pullPaymentIds,
            CreatedDate = DateTimeOffset.UtcNow,
            CardHeadline = model.CardHeadline,
            CardText = model.CardText,
            QrLogo = model.QrLogo
        });
        await SaveSettings(settings);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"Created tipcard set \"{model.Name}\" with {model.NumberOfCards} cards.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ViewSet), new { storeId, setId });
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ViewSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        await using var ctx = _dbContextFactory.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var vm = new TipcardSetDetailViewModel
        {
            SetId = set.Id,
            Name = set.Name,
            SatsPerCard = set.SatsPerCard,
            CreatedDate = set.CreatedDate,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            QrLogo = set.QrLogo,
            LightningConfigured = HasLightningPayouts()
        };

        foreach (var ppId in set.PullPaymentIds)
        {
            var pp = await ctx.PullPayments
                .Include(p => p.Payouts)
                .FirstOrDefaultAsync(p => p.Id == ppId);

            if (pp == null) continue;

            var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
            var isClaimed = progress.CompletedPercent > 0 || progress.AwaitingPercent > 0;

            if (isClaimed)
            {
                vm.ClaimedCount++;
                vm.ClaimedSats += set.SatsPerCard;
            }
            else
            {
                vm.FundedCount++;
                vm.FundedSats += set.SatsPerCard;
            }

            vm.Cards.Add(new TipcardViewModel
            {
                PullPaymentId = ppId,
                Sats = set.SatsPerCard,
                IsClaimed = isClaimed,
                ClaimUrl = BuildClaimUrl(ppId),
                LnurlBech32 = GetLnurlBech32(ppId)
            });
        }

        return View(vm);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> EditSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        var claimedCount = await CountClaimedCards(set.PullPaymentIds);

        return View(new EditTipcardSetViewModel
        {
            SetId = set.Id,
            Name = set.Name,
            SatsPerCard = set.SatsPerCard,
            NumberOfCards = set.NumberOfCards,
            ClaimedCount = claimedCount,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            QrLogo = set.QrLogo
        });
    }

    [HttpPost("~/plugins/{storeId}/tipcards/{setId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> EditSet(string storeId, string setId, EditTipcardSetViewModel model)
    {
        if (CurrentStore == null)
            return NotFound();

        if (!ModelState.IsValid)
            return View(model);

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        var satsChanged = model.SatsPerCard != set.SatsPerCard;
        var countChanged = model.NumberOfCards != set.NumberOfCards;

        if (satsChanged || countChanged)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;

            var claimedIds = new List<string>();
            var unclaimedIds = new List<string>();

            foreach (var ppId in set.PullPaymentIds)
            {
                var pp = await ctx.PullPayments
                    .Include(p => p.Payouts)
                    .FirstOrDefaultAsync(p => p.Id == ppId);
                if (pp == null) continue;

                var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
                if (progress.CompletedPercent > 0 || progress.AwaitingPercent > 0)
                    claimedIds.Add(ppId);
                else
                    unclaimedIds.Add(ppId);
            }

            if (model.NumberOfCards < claimedIds.Count)
            {
                ModelState.AddModelError(nameof(model.NumberOfCards),
                    $"Cannot reduce below {claimedIds.Count} (already claimed).");
                model.ClaimedCount = claimedIds.Count;
                return View(model);
            }

            var targetUnclaimed = model.NumberOfCards - claimedIds.Count;

            if (satsChanged)
            {
                foreach (var ppId in unclaimedIds)
                    await _pullPaymentHostedService.Cancel(new PullPaymentHostedService.CancelRequest(ppId));
                set.PullPaymentIds.RemoveAll(id => unclaimedIds.Contains(id));

                for (int i = 0; i < targetUnclaimed; i++)
                {
                    var ppId = await CreateCardPullPayment(set.Id, model.Name,
                        claimedIds.Count + i + 1, model.SatsPerCard / 100_000_000m);
                    set.PullPaymentIds.Add(ppId);
                }
            }
            else if (countChanged)
            {
                if (targetUnclaimed > unclaimedIds.Count)
                {
                    var toAdd = targetUnclaimed - unclaimedIds.Count;
                    for (int i = 0; i < toAdd; i++)
                    {
                        var ppId = await CreateCardPullPayment(set.Id, model.Name,
                            set.PullPaymentIds.Count + i + 1, set.SatsPerCard / 100_000_000m);
                        set.PullPaymentIds.Add(ppId);
                    }
                }
                else if (targetUnclaimed < unclaimedIds.Count)
                {
                    var toRemove = unclaimedIds.Count - targetUnclaimed;
                    var idsToRemove = unclaimedIds.TakeLast(toRemove).ToList();
                    foreach (var ppId in idsToRemove)
                        await _pullPaymentHostedService.Cancel(new PullPaymentHostedService.CancelRequest(ppId));
                    set.PullPaymentIds.RemoveAll(id => idsToRemove.Contains(id));
                }
            }

            set.SatsPerCard = model.SatsPerCard;
            set.NumberOfCards = model.NumberOfCards;
        }

        set.Name = model.Name;
        set.CardHeadline = model.CardHeadline;
        set.CardText = model.CardText;
        set.QrLogo = model.QrLogo;
        await SaveSettings(settings);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Tipcard set updated.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ViewSet), new { storeId, setId });
    }

    [HttpGet("~/plugins/tipcards/claim/{pullPaymentId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimCard(string pullPaymentId)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments
            .Include(p => p.Payouts)
            .FirstOrDefaultAsync(p => p.Id == pullPaymentId && !p.Archived);

        if (pp == null)
            return NotFound();

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Tipcard"))
            return NotFound();

        var now = DateTimeOffset.UtcNow;
        var store = await _storeRepository.FindStore(pp.StoreId);
        var storeBlob = store.GetStoreBlob();
        var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
        var isClaimed = progress.CompletedPercent > 0 || progress.AwaitingPercent > 0;
        var supportsLnurl = _pullPaymentHostedService.SupportsLNURL(pp, blob);

        var settings = await _storeRepository.GetSettingAsync<TipcardsStoreSettings>(pp.StoreId, SettingsKey)
                       ?? new TipcardsStoreSettings();
        settings.WalletRecommendations ??= TipcardsStoreSettings.DefaultWalletRecommendations;

        var setData = FindSetForPullPayment(settings, pullPaymentId);

        var sats = (long)(pp.Limit * 100_000_000m);
        var pullPaymentUrl = Url.Action("ViewPullPayment", "UIPullPayment",
            new { pullPaymentId }, Request.Scheme, Request.Host.ToString());

        var vm = new TipcardClaimViewModel
        {
            PullPaymentId = pp.Id,
            Sats = sats,
            StoreName = store.StoreName,
            SupportsLNURL = supportsLnurl,
            IsClaimed = isClaimed,
            LnurlBech32 = supportsLnurl ? GetLnurlBech32(pullPaymentId) : null,
            PullPaymentUrl = pullPaymentUrl,
            Headline = setData?.CardHeadline ?? "You received a tip!",
            CardText = setData?.CardText ?? "Scan this QR code with a Lightning wallet to claim your sats.",
            QrLogo = setData?.QrLogo ?? QrLogoType.Bitcoin,
            ShowWalletRecommendations = settings.ShowWalletRecommendations,
            WalletRecommendations = settings.WalletRecommendations
        };

        var fiatResult = await GetFiatValue(sats, storeBlob, pp.StoreId);
        if (fiatResult != null)
        {
            vm.FiatAmount = fiatResult.Value.amount;
            vm.FiatCurrency = fiatResult.Value.currency;
        }

        var branding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);
        vm.LogoUrl = branding.LogoUrl;
        vm.CssUrl = branding.CssUrl;
        vm.BrandColor = branding.BrandColor;

        return View(vm);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/print")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> PrintSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        var store = await _storeRepository.FindStore(storeId);
        var storeBlob = store.GetStoreBlob();
        var branding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);

        await using var ctx = _dbContextFactory.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var vm = new PrintTipcardSetViewModel
        {
            SetName = set.Name,
            SatsPerCard = set.SatsPerCard,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            StoreName = store.StoreName,
            LogoUrl = branding.LogoUrl,
            QrLogo = set.QrLogo
        };

        foreach (var ppId in set.PullPaymentIds)
        {
            var pp = await ctx.PullPayments
                .Include(p => p.Payouts)
                .FirstOrDefaultAsync(p => p.Id == ppId);

            if (pp == null) continue;

            var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
            var isClaimed = progress.CompletedPercent > 0 || progress.AwaitingPercent > 0;

            vm.Cards.Add(new PrintTipcardItem
            {
                PullPaymentId = ppId,
                ClaimUrl = BuildClaimUrl(ppId),
                LnurlBech32 = GetLnurlBech32(ppId),
                Sats = set.SatsPerCard,
                IsClaimed = isClaimed
            });
        }

        return View(vm);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        return View("Confirm", new ConfirmModel(
            "Delete Tipcard Set",
            $"This will archive all {set.NumberOfCards} pull payments in the set \"{set.Name}\". Are you sure?",
            "Delete"));
    }

    [HttpPost("~/plugins/{storeId}/tipcards/{setId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteSetPost(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(s => s.Id == setId);
        if (set == null)
            return NotFound();

        foreach (var ppId in set.PullPaymentIds)
        {
            await _pullPaymentHostedService.Cancel(new PullPaymentHostedService.CancelRequest(ppId));
        }

        settings.Sets.Remove(set);
        await SaveSettings(settings);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"Tipcard set \"{set.Name}\" deleted.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListSets), new { storeId });
    }

    [HttpGet("~/plugins/{storeId}/tipcards/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        return View(new TipcardsSettingsViewModel
        {
            ShowWalletRecommendations = settings.ShowWalletRecommendations,
            WalletRecommendationsJson = JsonConvert.SerializeObject(
                settings.WalletRecommendations ?? TipcardsStoreSettings.DefaultWalletRecommendations,
                Formatting.Indented)
        });
    }

    [HttpPost("~/plugins/{storeId}/tipcards/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Settings(string storeId, TipcardsSettingsViewModel model)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        settings.ShowWalletRecommendations = model.ShowWalletRecommendations;

        if (!string.IsNullOrWhiteSpace(model.WalletRecommendationsJson))
        {
            try
            {
                settings.WalletRecommendations = JsonConvert.DeserializeObject<List<WalletRecommendation>>(model.WalletRecommendationsJson);
            }
            catch
            {
                TempData[WellKnownTempData.ErrorMessage] = "Invalid wallet recommendations JSON.";
                return View(model);
            }
        }

        await SaveSettings(settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Tipcards settings updated.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    private bool HasLightningPayouts()
    {
        var pm = PayoutMethodId.Parse("BTC-LN");
        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
        return paymentMethods.Contains(pm);
    }

    private async Task<(decimal amount, string currency)?> GetFiatValue(long sats, StoreBlob storeBlob, string storeId)
    {
        var defaultCurrency = storeBlob.DefaultCurrency;
        if (string.IsNullOrEmpty(defaultCurrency) || defaultCurrency == "BTC" || defaultCurrency == "SATS")
            return null;

        try
        {
            var rate = await _rateFetcher.FetchRate(
                new CurrencyPair("BTC", defaultCurrency),
                storeBlob.GetRateRules(_defaultRulesCollection),
                new StoreIdRateContext(storeId),
                CancellationToken.None);

            if (rate.BidAsk == null)
                return null;

            var btcAmount = sats / 100_000_000m;
            var fiatAmount = Math.Round(btcAmount * rate.BidAsk.Bid, 2);
            return (fiatAmount, defaultCurrency);
        }
        catch
        {
            return null;
        }
    }

    private string GetLnurlBech32(string pullPaymentId)
    {
        var cryptoCode = _networkProvider.DefaultNetwork?.CryptoCode ?? "BTC";
        var lnurlEndpoint = new Uri(Url.Action("GetLNURLForPullPayment", "UILNURL",
            new { cryptoCode, pullPaymentId },
            Request.Scheme, Request.Host.ToString())!);
        return LNURL.LNURL.EncodeUri(lnurlEndpoint, "withdrawRequest", true).ToString().ToUpperInvariant();
    }

    private string BuildClaimUrl(string pullPaymentId)
    {
        var baseClaimUrl = Url.Action(nameof(ClaimCard), "Tipcards",
            new { pullPaymentId }, Request.Scheme, Request.Host.ToString());
        var lnurl = GetLnurlBech32(pullPaymentId);
        return $"{baseClaimUrl}?lightning={lnurl}";
    }

    private static TipcardSetData FindSetForPullPayment(TipcardsStoreSettings settings, string pullPaymentId)
    {
        return settings.Sets.FirstOrDefault(s => s.PullPaymentIds.Contains(pullPaymentId));
    }

    private async Task<TipcardsStoreSettings> GetSettings()
    {
        var settings = await _storeRepository.GetSettingAsync<TipcardsStoreSettings>(CurrentStore.Id, SettingsKey)
                       ?? new TipcardsStoreSettings();
        settings.WalletRecommendations ??= TipcardsStoreSettings.DefaultWalletRecommendations;
        return settings;
    }

    private async Task SaveSettings(TipcardsStoreSettings settings)
    {
        await _storeRepository.UpdateSetting(CurrentStore.Id, SettingsKey, settings);
    }

    private async Task<string> CreateCardPullPayment(string setId, string setName, int cardNumber, decimal amountInBtc)
    {
        var selectedPaymentMethodIds = new[] { PayoutMethodId.Parse("BTC-CHAIN"), PayoutMethodId.Parse("BTC-LN") };
        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());

        return await _pullPaymentHostedService.CreatePullPayment(HttpContext.GetStoreData(), new()
        {
            Amount = amountInBtc,
            Currency = "BTC",
            Name = $"Tipcard {setName} #{cardNumber}",
            Description = $"tipcard-set:{setId}",
            PayoutMethods = selectedPaymentMethodIds
                .Where(id => paymentMethods.Contains(id))
                .Select(c => c.ToString()).ToArray(),
            BOLT11Expiration = TimeSpan.FromDays(365),
            AutoApproveClaims = true
        });
    }

    private async Task<int> CountClaimedCards(List<string> pullPaymentIds)
    {
        if (!pullPaymentIds.Any()) return 0;

        await using var ctx = _dbContextFactory.CreateContext();
        var now = DateTimeOffset.UtcNow;
        int claimed = 0;

        foreach (var ppId in pullPaymentIds)
        {
            var pp = await ctx.PullPayments
                .Include(p => p.Payouts)
                .FirstOrDefaultAsync(p => p.Id == ppId);

            if (pp == null) continue;

            var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
            if (progress.CompletedPercent > 0 || progress.AwaitingPercent > 0)
                claimed++;
        }

        return claimed;
    }
}
