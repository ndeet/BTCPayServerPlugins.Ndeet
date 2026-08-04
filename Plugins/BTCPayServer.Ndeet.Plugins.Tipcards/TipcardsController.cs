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
using BTCPayServer.Models;
using BTCPayServer.Ndeet.Plugins.Tipcards.ViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using PayoutState = BTCPayServer.Client.Models.PayoutState;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public class TipcardsController : Controller
{
    private readonly StoreRepository _storeRepository;
    private readonly TipcardService _tipcardService;
    private readonly TipcardStoreLock _storeLock;
    private readonly UILNURLController _lnurlController;
    private readonly UriResolver _uriResolver;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly RateFetcher _rateFetcher;
    private readonly DefaultRulesCollection _defaultRulesCollection;

    public TipcardsController(
        StoreRepository storeRepository,
        TipcardService tipcardService,
        TipcardStoreLock storeLock,
        UILNURLController lnurlController,
        UriResolver uriResolver,
        BTCPayNetworkProvider networkProvider,
        RateFetcher rateFetcher,
        DefaultRulesCollection defaultRulesCollection)
    {
        _storeRepository = storeRepository;
        _tipcardService = tipcardService;
        _storeLock = storeLock;
        _lnurlController = lnurlController;
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
        var pullPayments = await _tipcardService.GetPullPaymentsAsync(
            settings.Sets.SelectMany(set => set.Cards));
        var vm = new ListTipcardSetsViewModel
        {
            LightningConfigured = _tipcardService.HasLightningPayouts(CurrentStore)
        };

        foreach (var set in settings.Sets.OrderByDescending(set => set.CreatedDate))
        {
            var claimedCount = set.Cards.Count(card =>
                TryGetPullPayment(card, pullPayments, out var pullPayment) &&
                IsPullPaymentClaimed(pullPayment));
            var totalSats = set.Cards.Sum(card =>
                TryGetPullPayment(card, pullPayments, out var pullPayment)
                    ? GetSats(pullPayment)
                    : set.SatsPerCard);
            vm.Sets.Add(new TipcardSetViewModel
            {
                Id = set.Id,
                Name = set.Name,
                TotalCards = set.Cards.Count,
                ClaimedCards = claimedCount,
                SatsPerCard = set.SatsPerCard,
                TotalSats = totalSats,
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

        if (!_tipcardService.HasLightningPayouts(CurrentStore))
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

        if (!_tipcardService.HasLightningPayouts(CurrentStore))
        {
            TempData[WellKnownTempData.ErrorMessage] = "You must enable Lightning payouts before creating tipcards.";
            return RedirectToAction(nameof(ListSets), new { storeId });
        }

        var setId = Encoders.Base58.EncodeData(RandomUtils.GetBytes(8));
        var cards = TipcardService.CreateCards(model.NumberOfCards);

        using (await _storeLock.LockAsync(CurrentStore.Id, HttpContext.RequestAborted))
        {
            var settings = await GetSettings();
            settings.Sets.Add(new TipcardSetData
            {
                Id = setId,
                Name = model.Name,
                SatsPerCard = model.SatsPerCard,
                NumberOfCards = cards.Count,
                Cards = cards,
                CreatedDate = DateTimeOffset.UtcNow,
                CardHeadline = model.CardHeadline,
                CardText = model.CardText,
                QrLogo = model.QrLogo
            });
            await _tipcardService.SaveSettingsAsync(CurrentStore.Id, settings);
        }

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
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        if (set == null)
            return NotFound();

        var pullPayments = await _tipcardService.GetPullPaymentsAsync(set.Cards);
        var vm = new TipcardSetDetailViewModel
        {
            SetId = set.Id,
            Name = set.Name,
            SatsPerCard = set.SatsPerCard,
            CreatedDate = set.CreatedDate,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            QrLogo = set.QrLogo,
            LightningConfigured = _tipcardService.HasLightningPayouts(CurrentStore)
        };

        foreach (var card in set.Cards.OrderBy(card => card.CardNumber))
        {
            var hasPullPayment = TryGetPullPayment(card, pullPayments, out var pullPayment);
            var isClaimed = hasPullPayment && IsPullPaymentClaimed(pullPayment);
            var isUnavailable = !isClaimed &&
                                !string.IsNullOrEmpty(card.PullPaymentId) &&
                                (!hasPullPayment || !pullPayment.IsRunning());
            var sats = hasPullPayment ? GetSats(pullPayment) : set.SatsPerCard;

            if (isClaimed)
            {
                vm.ClaimedCount++;
                vm.ClaimedSats += sats;
            }
            else if (isUnavailable)
            {
                vm.UnavailableCount++;
            }
            else
            {
                vm.AvailableCount++;
                vm.AvailableSats += sats;
            }

            vm.Cards.Add(new TipcardViewModel
            {
                ClaimId = card.ClaimId,
                CardNumber = card.CardNumber,
                PullPaymentId = card.PullPaymentId,
                Sats = sats,
                IsActivated = !string.IsNullOrEmpty(card.PullPaymentId),
                IsClaimed = isClaimed,
                IsUnavailable = isUnavailable,
                ClaimUrl = BuildClaimUrl(CurrentStore.Id, card.ClaimId),
                LnurlBech32 = GetLnurlBech32(CurrentStore.Id, card.ClaimId)
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
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        if (set == null)
            return NotFound();

        return View(new EditTipcardSetViewModel
        {
            SetId = set.Id,
            Name = set.Name,
            SatsPerCard = set.SatsPerCard,
            NumberOfCards = set.Cards.Count,
            ClaimedCount = await CountClaimedCards(set.Cards),
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

        model.SetId = setId;
        if (!ModelState.IsValid)
        {
            model.ClaimedCount = await GetClaimedCountForSet(setId);
            return View(model);
        }

        using (await _storeLock.LockAsync(CurrentStore.Id, HttpContext.RequestAborted))
        {
            var settings = await GetSettings();
            var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
            if (set == null)
                return NotFound();

            var pullPayments = await _tipcardService.GetPullPaymentsAsync(set.Cards);
            var claimedClaimIds = set.Cards
                .Where(card => TryGetPullPayment(card, pullPayments, out var pullPayment) &&
                               IsPullPaymentClaimed(pullPayment))
                .Select(card => card.ClaimId)
                .ToHashSet(StringComparer.Ordinal);

            if (model.NumberOfCards < claimedClaimIds.Count)
            {
                ModelState.AddModelError(nameof(model.NumberOfCards),
                    $"Cannot reduce below {claimedClaimIds.Count} (already claimed).");
                model.ClaimedCount = claimedClaimIds.Count;
                return View(model);
            }

            var satsChanged = model.SatsPerCard != set.SatsPerCard;
            var cardsToRemove = set.Cards
                .Where(card => !claimedClaimIds.Contains(card.ClaimId))
                .Reverse()
                .Take(Math.Max(0, set.Cards.Count - model.NumberOfCards))
                .ToList();
            var removedClaimIds = cardsToRemove
                .Select(card => card.ClaimId)
                .ToHashSet(StringComparer.Ordinal);

            var pullPaymentsToCancel = cardsToRemove
                .Select(card => card.PullPaymentId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (satsChanged)
            {
                pullPaymentsToCancel.AddRange(set.Cards
                    .Where(card => !claimedClaimIds.Contains(card.ClaimId))
                    .Select(card => card.PullPaymentId)
                    .Where(id => !string.IsNullOrEmpty(id)));
            }

            await _tipcardService.CancelPullPaymentsAsync(CurrentStore.Id, pullPaymentsToCancel);

            set.Cards.RemoveAll(card => removedClaimIds.Contains(card.ClaimId));
            if (satsChanged)
            {
                foreach (var card in set.Cards.Where(card => !claimedClaimIds.Contains(card.ClaimId)))
                    card.PullPaymentId = null;
            }

            if (model.NumberOfCards > set.Cards.Count)
            {
                var nextCardNumber = set.Cards.Count == 0
                    ? 1
                    : set.Cards.Max(card => card.CardNumber) + 1;
                set.Cards.AddRange(TipcardService.CreateCards(
                    model.NumberOfCards - set.Cards.Count,
                    nextCardNumber));
            }

            set.Name = model.Name;
            set.SatsPerCard = model.SatsPerCard;
            set.NumberOfCards = set.Cards.Count;
            set.CardHeadline = model.CardHeadline;
            set.CardText = model.CardText;
            set.QrLogo = model.QrLogo;
            await _tipcardService.SaveSettingsAsync(CurrentStore.Id, settings);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Tipcard set updated.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ViewSet), new { storeId, setId });
    }

    [HttpGet("~/plugins/tipcards/claim/{storeId}/{claimId}")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ClaimCard(string storeId, string claimId, CancellationToken cancellationToken)
    {
        var activation = await _tipcardService.EnsurePullPaymentAsync(storeId, claimId, cancellationToken);
        if (activation.Status == TipcardActivationStatus.NotFound)
            return NotFound();

        var store = activation.Store;
        var set = activation.Set;
        var pullPayment = activation.PullPayment;
        var storeBlob = store.GetStoreBlob();
        var settings = await _tipcardService.GetSettingsAsync(storeId);
        var isClaimed = pullPayment != null && IsPullPaymentClaimed(pullPayment);
        var supportsLnurl = activation.Status == TipcardActivationStatus.Ready &&
                            activation.LightningConfigured &&
                            _tipcardService.SupportsLnurl(pullPayment);
        var sats = pullPayment == null ? set.SatsPerCard : GetSats(pullPayment);
        var pullPaymentUrl = pullPayment == null
            ? null
            : Url.Action("ViewPullPayment", "UIPullPayment",
                new { pullPaymentId = pullPayment.Id }, Request.Scheme, Request.Host.ToString());

        var vm = new TipcardClaimViewModel
        {
            PullPaymentId = pullPayment?.Id,
            Sats = sats,
            StoreName = store.StoreName,
            SupportsLNURL = supportsLnurl,
            IsClaimed = isClaimed,
            LnurlBech32 = supportsLnurl ? GetLnurlBech32(storeId, claimId) : null,
            PullPaymentUrl = pullPaymentUrl,
            Headline = set.CardHeadline,
            CardText = set.CardText,
            QrLogo = set.QrLogo,
            ShowWalletRecommendations = settings.ShowWalletRecommendations,
            WalletRecommendations = settings.WalletRecommendations,
            UnavailableMessage = GetUnavailableMessage(activation.Status)
        };

        var fiatResult = await GetFiatValue(sats, storeBlob, storeId);
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

    [EnableCors(CorsPolicies.All)]
    [HttpGet("~/plugins/tipcards/withdraw/{storeId}/{claimId}")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> WithdrawCard(
        string storeId,
        string claimId,
        [FromQuery] string pr,
        CancellationToken cancellationToken)
    {
        var activation = await _tipcardService.EnsurePullPaymentAsync(storeId, claimId, cancellationToken);
        if (activation.Status == TipcardActivationStatus.NotFound)
            return NotFound();

        if (activation.Status != TipcardActivationStatus.Ready ||
            !activation.LightningConfigured ||
            !_tipcardService.SupportsLnurl(activation.PullPayment))
        {
            return BadRequest(new LNUrlStatusResponse
            {
                Status = "ERROR",
                Reason = GetUnavailableMessage(activation.Status)
            });
        }

        var cryptoCode = _networkProvider.DefaultNetwork?.CryptoCode;
        if (string.IsNullOrEmpty(cryptoCode))
        {
            return BadRequest(new LNUrlStatusResponse
            {
                Status = "ERROR",
                Reason = "Lightning withdrawals are unavailable right now."
            });
        }

        _lnurlController.ControllerContext.HttpContext = HttpContext;
        return await _lnurlController.GetLNURLForPullPayment(
            cryptoCode,
            activation.PullPayment.Id,
            pr,
            cancellationToken);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/print")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> PrintSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        if (set == null)
            return NotFound();

        var store = await _storeRepository.FindStore(CurrentStore.Id);
        var storeBlob = store.GetStoreBlob();
        var branding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);
        var pullPayments = await _tipcardService.GetPullPaymentsAsync(set.Cards);

        var vm = new PrintTipcardSetViewModel
        {
            SetName = set.Name,
            SatsPerCard = set.SatsPerCard,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            LogoUrl = branding.LogoUrl,
            QrLogo = set.QrLogo
        };

        foreach (var card in set.Cards.OrderBy(card => card.CardNumber))
        {
            var hasPullPayment = TryGetPullPayment(card, pullPayments, out var pullPayment);
            var isClaimed = hasPullPayment && IsPullPaymentClaimed(pullPayment);
            vm.Cards.Add(new PrintTipcardItem
            {
                ClaimId = card.ClaimId,
                CardNumber = card.CardNumber,
                ClaimUrl = BuildClaimUrl(CurrentStore.Id, card.ClaimId),
                Sats = hasPullPayment ? GetSats(pullPayment) : set.SatsPerCard,
                IsClaimed = isClaimed,
                IsUnavailable = !isClaimed &&
                                !string.IsNullOrEmpty(card.PullPaymentId) &&
                                (!hasPullPayment || !pullPayment.IsRunning())
            });
        }

        return View(vm);
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/pdf")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DownloadPdf(
        string storeId,
        string setId,
        string paper = "A4",
        int columns = 3,
        bool markers = true,
        double? customW = null,
        double? customH = null)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        if (set == null)
            return NotFound();

        var pullPayments = await _tipcardService.GetPullPaymentsAsync(set.Cards);
        var (pageW, pageH) = paper switch
        {
            "A3" => (297.0, 420.0),
            "letter" => (215.9, 279.4),
            "custom" => (customW ?? 210, customH ?? 297),
            _ => (210.0, 297.0)
        };

        var pdfRequest = new TipcardPdfRequest
        {
            PageWidthMm = pageW,
            PageHeightMm = pageH,
            Columns = Math.Clamp(columns, 1, 10),
            CuttingMarkers = markers,
            SetName = set.Name,
            CardHeadline = set.CardHeadline,
            CardText = set.CardText,
            QrLogo = set.QrLogo
        };

        foreach (var card in set.Cards.OrderBy(card => card.CardNumber))
        {
            var hasPullPayment = TryGetPullPayment(card, pullPayments, out var pullPayment);
            var isUnavailable = !string.IsNullOrEmpty(card.PullPaymentId) &&
                                (!hasPullPayment || !pullPayment.IsRunning());
            if (isUnavailable || hasPullPayment && IsPullPaymentClaimed(pullPayment))
                continue;

            pdfRequest.Cards.Add(new TipcardPdfItem
            {
                ClaimUrl = BuildClaimUrl(CurrentStore.Id, card.ClaimId),
                Sats = hasPullPayment ? GetSats(pullPayment) : set.SatsPerCard
            });
        }

        try
        {
            var pdfBytes = TipcardPdfGenerator.Generate(pdfRequest);
            var filename = set.Name.Replace(" ", "_") + "_tipcards.pdf";
            return File(pdfBytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Failed to generate PDF: {ex.Message}";
            return RedirectToAction(nameof(ViewSet), new { storeId, setId });
        }
    }

    [HttpGet("~/plugins/{storeId}/tipcards/{setId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteSet(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        if (set == null)
            return NotFound();

        var activatedCount = set.Cards.Count(card => !string.IsNullOrEmpty(card.PullPaymentId));
        return View("Confirm", new ConfirmModel(
            "Delete Tipcard Set",
            $"This will archive {activatedCount} activated pull payments and invalidate all cards in the set \"{set.Name}\". Are you sure?",
            "Delete"));
    }

    [HttpPost("~/plugins/{storeId}/tipcards/{setId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteSetPost(string storeId, string setId)
    {
        if (CurrentStore == null)
            return NotFound();

        string setName;
        using (await _storeLock.LockAsync(CurrentStore.Id, HttpContext.RequestAborted))
        {
            var settings = await GetSettings();
            var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
            if (set == null)
                return NotFound();

            await _tipcardService.CancelPullPaymentsAsync(CurrentStore.Id,
                set.Cards.Select(card => card.PullPaymentId));
            setName = set.Name;
            settings.Sets.Remove(set);
            await _tipcardService.SaveSettingsAsync(CurrentStore.Id, settings);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"Tipcard set \"{setName}\" deleted.",
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
                settings.WalletRecommendations,
                Formatting.Indented)
        });
    }

    [HttpPost("~/plugins/{storeId}/tipcards/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Settings(string storeId, TipcardsSettingsViewModel model)
    {
        if (CurrentStore == null)
            return NotFound();

        List<WalletRecommendation> walletRecommendations = null;
        if (!string.IsNullOrWhiteSpace(model.WalletRecommendationsJson))
        {
            try
            {
                walletRecommendations = JsonConvert.DeserializeObject<List<WalletRecommendation>>(
                    model.WalletRecommendationsJson);
            }
            catch
            {
                TempData[WellKnownTempData.ErrorMessage] = "Invalid wallet recommendations JSON.";
                return View(model);
            }
        }

        using (await _storeLock.LockAsync(CurrentStore.Id, HttpContext.RequestAborted))
        {
            var settings = await GetSettings();
            settings.ShowWalletRecommendations = model.ShowWalletRecommendations;
            if (walletRecommendations != null)
                settings.WalletRecommendations = walletRecommendations;
            await _tipcardService.SaveSettingsAsync(CurrentStore.Id, settings);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Tipcards settings updated.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    private async Task<(decimal amount, string currency)?> GetFiatValue(
        long sats,
        StoreBlob storeBlob,
        string storeId)
    {
        var defaultCurrency = storeBlob.DefaultCurrency;
        if (string.IsNullOrEmpty(defaultCurrency) || defaultCurrency is "BTC" or "SATS")
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

    private string GetLnurlBech32(string storeId, string claimId)
    {
        var lnurlEndpoint = new Uri(Url.Action(nameof(WithdrawCard), "Tipcards",
            new { storeId, claimId },
            Request.Scheme,
            Request.Host.ToString())!);
        return LNURL.LNURL.EncodeUri(lnurlEndpoint, "withdrawRequest", true)
            .ToString()
            .ToUpperInvariant();
    }

    private string BuildClaimUrl(string storeId, string claimId)
    {
        var baseClaimUrl = Url.Action(nameof(ClaimCard), "Tipcards",
            new { storeId, claimId },
            Request.Scheme,
            Request.Host.ToString());
        return $"{baseClaimUrl}?lightning={GetLnurlBech32(storeId, claimId)}";
    }

    private static bool TryGetPullPayment(
        TipcardData card,
        IReadOnlyDictionary<string, PullPaymentData> pullPayments,
        out PullPaymentData pullPayment)
    {
        pullPayment = null;
        return !string.IsNullOrEmpty(card.PullPaymentId) &&
               pullPayments.TryGetValue(card.PullPaymentId, out pullPayment);
    }

    public static bool IsPullPaymentClaimed(PullPaymentData pullPayment)
    {
        var payouts = pullPayment.Payouts ?? new List<PayoutData>();
        var completed = payouts
            .Where(payout => payout.State is PayoutState.Completed or PayoutState.InProgress)
            .Sum(payout => payout.OriginalAmount);
        if (completed > 0)
            return true;

        var awaiting = payouts
            .Where(payout => payout.State is PayoutState.AwaitingPayment or PayoutState.AwaitingApproval)
            .Sum(payout => payout.OriginalAmount);
        return awaiting > 0;
    }

    private static long GetSats(PullPaymentData pullPayment)
    {
        return (long)(pullPayment.Limit * 100_000_000m);
    }

    private static string GetUnavailableMessage(TipcardActivationStatus status)
    {
        return status switch
        {
            TipcardActivationStatus.LightningUnavailable =>
                "This tipcard cannot be claimed right now because Lightning payouts are not configured. Please contact the person who gave you this card.",
            TipcardActivationStatus.PullPaymentUnavailable =>
                "This tipcard is unavailable. Please contact the person who gave you this card.",
            TipcardActivationStatus.Failed =>
                "This tipcard could not be prepared right now. Please try again later.",
            _ => "Lightning withdrawals are unavailable right now."
        };
    }

    private Task<TipcardsStoreSettings> GetSettings()
    {
        return _tipcardService.GetSettingsAsync(CurrentStore.Id);
    }

    private async Task<int> CountClaimedCards(IEnumerable<TipcardData> cards)
    {
        var cardList = cards.ToList();
        var pullPayments = await _tipcardService.GetPullPaymentsAsync(cardList);
        return cardList.Count(card =>
            TryGetPullPayment(card, pullPayments, out var pullPayment) &&
            IsPullPaymentClaimed(pullPayment));
    }

    private async Task<int> GetClaimedCountForSet(string setId)
    {
        var settings = await GetSettings();
        var set = settings.Sets.FirstOrDefault(candidate => candidate.Id == setId);
        return set == null ? 0 : await CountClaimedCards(set.Cards);
    }
}
