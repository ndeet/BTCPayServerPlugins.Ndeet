using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Ndeet.Plugins.Tipcards.ViewModels;

public class TipcardSetViewModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int TotalCards { get; set; }
    public int ClaimedCards { get; set; }
    public long SatsPerCard { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListTipcardSetsViewModel
{
    public List<TipcardSetViewModel> Sets { get; set; } = new();
    public bool LightningConfigured { get; set; }
}

public class CreateTipcardSetViewModel
{
    [Required]
    [Display(Name = "Set Name")]
    public string Name { get; set; }

    [Required]
    [Range(1, 1_000_000_000)]
    [Display(Name = "Sats per card")]
    public long SatsPerCard { get; set; } = 1000;

    [Required]
    [Range(1, 500)]
    [Display(Name = "Number of cards")]
    public int NumberOfCards { get; set; } = 10;

    [Display(Name = "Card Headline")]
    public string CardHeadline { get; set; } = "You received a tip!";

    [Display(Name = "Card Text")]
    public string CardText { get; set; } = "Scan this QR code with a Lightning wallet to claim your sats.";

    [Display(Name = "Logo on QR Code")]
    public QrLogoType QrLogo { get; set; } = QrLogoType.Bitcoin;
}

public class TipcardSetDetailViewModel
{
    public string SetId { get; set; }
    public string Name { get; set; }
    public long SatsPerCard { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public string CardHeadline { get; set; }
    public string CardText { get; set; }
    public QrLogoType QrLogo { get; set; }
    public int ClaimedCount { get; set; }
    public long ClaimedSats { get; set; }
    public int FundedCount { get; set; }
    public long FundedSats { get; set; }
    public bool LightningConfigured { get; set; }
    public List<TipcardViewModel> Cards { get; set; } = new();
}

public class EditTipcardSetViewModel
{
    public string SetId { get; set; }

    [Required]
    [Display(Name = "Set Name")]
    public string Name { get; set; }

    [Required]
    [Range(1, 1_000_000_000)]
    [Display(Name = "Sats per card")]
    public long SatsPerCard { get; set; }

    [Required]
    [Range(1, 500)]
    [Display(Name = "Number of cards")]
    public int NumberOfCards { get; set; }

    public int ClaimedCount { get; set; }

    [Display(Name = "Card Headline")]
    public string CardHeadline { get; set; }

    [Display(Name = "Card Text")]
    public string CardText { get; set; }

    [Display(Name = "Logo on QR Code")]
    public QrLogoType QrLogo { get; set; }
}

public class TipcardViewModel
{
    public string PullPaymentId { get; set; }
    public long Sats { get; set; }
    public bool IsClaimed { get; set; }
    public string ClaimUrl { get; set; }
    public string LnurlBech32 { get; set; }
}

public class TipcardClaimViewModel
{
    public string PullPaymentId { get; set; }
    public long Sats { get; set; }
    public string StoreName { get; set; }
    public string LogoUrl { get; set; }
    public string BrandColor { get; set; }
    public string CssUrl { get; set; }
    public bool SupportsLNURL { get; set; }
    public bool IsClaimed { get; set; }
    public string LnurlBech32 { get; set; }
    public string PullPaymentUrl { get; set; }
    public string Headline { get; set; }
    public string CardText { get; set; }
    public QrLogoType QrLogo { get; set; }
    public bool ShowWalletRecommendations { get; set; }
    public List<WalletRecommendation> WalletRecommendations { get; set; } = new();
    public decimal? FiatAmount { get; set; }
    public string FiatCurrency { get; set; }
}

public class PrintTipcardSetViewModel
{
    public string SetName { get; set; }
    public long SatsPerCard { get; set; }
    public string CardHeadline { get; set; }
    public string CardText { get; set; }
    public string LogoUrl { get; set; }
    public QrLogoType QrLogo { get; set; }
    public List<PrintTipcardItem> Cards { get; set; } = new();
}

public class PrintTipcardItem
{
    public string PullPaymentId { get; set; }
    public string ClaimUrl { get; set; }
    public string LnurlBech32 { get; set; }
    public long Sats { get; set; }
    public bool IsClaimed { get; set; }
}

public class TipcardsSettingsViewModel
{
    [Display(Name = "Show wallet recommendations on claim page")]
    public bool ShowWalletRecommendations { get; set; }

    [Display(Name = "Wallet Recommendations (JSON)")]
    public string WalletRecommendationsJson { get; set; }
}
