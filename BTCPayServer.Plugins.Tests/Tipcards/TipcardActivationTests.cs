using System.Collections.Concurrent;
using BTCPayServer.Data;
using BTCPayServer.Ndeet.Plugins.Tipcards;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayServer.Plugins.Tests.Tipcards;

public class TipcardActivationTests
{
    [Fact]
    public async Task EnsurePullPaymentAsync_ConcurrentRequestsCreateOnePullPayment()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card));
        var cancellationToken = TestContext.Current.CancellationToken;

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => service.EnsurePullPaymentAsync("store-a", card.ClaimId, cancellationToken)));

        Assert.Equal(1, service.CreatedCount);
        Assert.All(results, result => Assert.Equal(TipcardActivationStatus.Ready, result.Status));
        Assert.Single(results.Select(result => result.PullPayment.Id).Distinct());
        Assert.Equal(results[0].PullPayment.Id, service.Settings.Sets.Single().Cards.Single().PullPaymentId);
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_ConcurrentCardsPreserveBothMappings()
    {
        var cards = TipcardService.CreateCards(2);
        var service = new FakeTipcardService(CreateSettings(cards.ToArray()));
        var cancellationToken = TestContext.Current.CancellationToken;

        var results = await Task.WhenAll(cards.Select(card =>
            service.EnsurePullPaymentAsync("store-a", card.ClaimId, cancellationToken)));

        Assert.Equal(2, service.CreatedCount);
        Assert.All(results, result => Assert.Equal(TipcardActivationStatus.Ready, result.Status));
        Assert.All(service.Settings.Sets.Single().Cards,
            persistedCard => Assert.False(string.IsNullOrEmpty(persistedCard.PullPaymentId)));
        Assert.Equal(2, service.Settings.Sets.Single().Cards
            .Select(persistedCard => persistedCard.PullPaymentId)
            .Distinct()
            .Count());
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_ReusesPersistedPullPayment()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card));
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await service.EnsurePullPaymentAsync("store-a", card.ClaimId, cancellationToken);
        var second = await service.EnsurePullPaymentAsync("store-a", card.ClaimId, cancellationToken);

        Assert.Equal(1, service.CreatedCount);
        Assert.Equal(first.PullPayment.Id, second.PullPayment.Id);
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_ArchivesNewPullPaymentWhenMappingSaveFails()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card)) { FailSave = true };

        var result = await service.EnsurePullPaymentAsync(
            "store-a",
            card.ClaimId,
            TestContext.Current.CancellationToken);

        Assert.Equal(TipcardActivationStatus.Failed, result.Status);
        Assert.Equal(1, service.CreatedCount);
        Assert.Equal(1, service.CancelledCount);
        Assert.Null(service.Settings.Sets.Single().Cards.Single().PullPaymentId);
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_DoesNotCreateWithoutLightning()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card)) { LightningConfigured = false };

        var result = await service.EnsurePullPaymentAsync(
            "store-a",
            card.ClaimId,
            TestContext.Current.CancellationToken);

        Assert.Equal(TipcardActivationStatus.LightningUnavailable, result.Status);
        Assert.Equal(0, service.CreatedCount);
        Assert.Null(service.Settings.Sets.Single().Cards.Single().PullPaymentId);
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_DoesNotCreateForUnknownClaimId()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card));

        var result = await service.EnsurePullPaymentAsync(
            "store-a",
            "unknown-claim-id",
            TestContext.Current.CancellationToken);

        Assert.Equal(TipcardActivationStatus.NotFound, result.Status);
        Assert.Equal(0, service.CreatedCount);
    }

    [Fact]
    public async Task EnsurePullPaymentAsync_DoesNotReplaceArchivedPullPayment()
    {
        var card = TipcardService.CreateCards(1).Single();
        var service = new FakeTipcardService(CreateSettings(card));

        var first = await service.EnsurePullPaymentAsync(
            "store-a",
            card.ClaimId,
            TestContext.Current.CancellationToken);
        first.PullPayment.Archived = true;

        var second = await service.EnsurePullPaymentAsync(
            "store-a",
            card.ClaimId,
            TestContext.Current.CancellationToken);

        Assert.Equal(TipcardActivationStatus.PullPaymentUnavailable, second.Status);
        Assert.Equal(1, service.CreatedCount);
        Assert.Equal(first.PullPayment.Id, service.Settings.Sets.Single().Cards.Single().PullPaymentId);
    }

    private static TipcardsStoreSettings CreateSettings(params TipcardData[] cards)
    {
        return new TipcardsStoreSettings
        {
            Sets =
            [
                new TipcardSetData
                {
                    Id = "set-a",
                    Name = "Test set",
                    SatsPerCard = 1_000,
                    NumberOfCards = cards.Length,
                    Cards = cards.ToList(),
                    CreatedDate = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    private sealed class FakeTipcardService : TipcardService
    {
        private readonly StoreData _store = new() { Id = "store-a", StoreName = "Test store" };
        private readonly ConcurrentDictionary<string, PullPaymentData> _pullPayments = new();
        private TipcardsStoreSettings _settings;
        private int _createdCount;
        private int _cancelledCount;

        public FakeTipcardService(TipcardsStoreSettings settings)
            : base(null!, null!, null!, null!, new TipcardStoreLock(), NullLogger<TipcardService>.Instance)
        {
            _settings = Clone(settings);
        }

        public int CreatedCount => _createdCount;
        public int CancelledCount => _cancelledCount;
        public bool FailSave { get; init; }
        public bool LightningConfigured { get; init; } = true;
        public TipcardsStoreSettings Settings => Clone(_settings);

        public override Task<TipcardsStoreSettings> GetSettingsAsync(string storeId)
        {
            return Task.FromResult(Clone(_settings));
        }

        public override Task SaveSettingsAsync(string storeId, TipcardsStoreSettings settings)
        {
            if (FailSave)
                throw new InvalidOperationException("Simulated settings write failure.");

            _settings = Clone(settings);
            return Task.CompletedTask;
        }

        public override bool HasLightningPayouts(StoreData store)
        {
            return LightningConfigured;
        }

        public override Task CancelPullPaymentsAsync(string storeId, IEnumerable<string> pullPaymentIds)
        {
            foreach (var id in pullPaymentIds.Where(id => !string.IsNullOrEmpty(id)))
            {
                if (_pullPayments.TryGetValue(id, out var pullPayment))
                {
                    pullPayment.Archived = true;
                    Interlocked.Increment(ref _cancelledCount);
                }
            }

            return Task.CompletedTask;
        }

        protected override Task<StoreData> FindStoreAsync(string storeId)
        {
            return Task.FromResult(storeId == _store.Id ? _store : null!);
        }

        protected override Task<PullPaymentData> GetPullPaymentAsync(string pullPaymentId)
        {
            _pullPayments.TryGetValue(pullPaymentId, out var pullPayment);
            return Task.FromResult(pullPayment!);
        }

        protected override async Task<string> CreatePullPaymentAsync(
            StoreData store,
            TipcardSetData set,
            TipcardData card)
        {
            Interlocked.Increment(ref _createdCount);
            await Task.Delay(20, TestContext.Current.CancellationToken);

            var id = $"pull-payment-{card.ClaimId}";
            _pullPayments[id] = new PullPaymentData
            {
                Id = id,
                StoreId = store.Id,
                Currency = "BTC",
                Limit = set.SatsPerCard / 100_000_000m,
                StartDate = DateTimeOffset.UtcNow.AddSeconds(-1),
                Payouts = []
            };
            return id;
        }

        private static TipcardsStoreSettings Clone(TipcardsStoreSettings source)
        {
            return new TipcardsStoreSettings
            {
                ShowWalletRecommendations = source.ShowWalletRecommendations,
                WalletRecommendations = source.WalletRecommendations,
                Sets = source.Sets.Select(set => new TipcardSetData
                {
                    Id = set.Id,
                    Name = set.Name,
                    SatsPerCard = set.SatsPerCard,
                    NumberOfCards = set.NumberOfCards,
                    CreatedDate = set.CreatedDate,
                    CardHeadline = set.CardHeadline,
                    CardText = set.CardText,
                    QrLogo = set.QrLogo,
                    Cards = set.Cards.Select(card => new TipcardData
                    {
                        ClaimId = card.ClaimId,
                        CardNumber = card.CardNumber,
                        PullPaymentId = card.PullPaymentId
                    }).ToList()
                }).ToList()
            };
        }
    }
}
