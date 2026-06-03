#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox.ViewModels;

public class AmbassadorToolboxIndexViewModel
{
    public bool EnableSiteBanner { get; set; }

    [StringLength(500)]
    [Display(Name = "Banner text")]
    public string SiteBannerText { get; set; } = AmbassadorToolboxSettings.DefaultSiteBannerText;

    [StringLength(7, MinimumLength = 4)]
    [RegularExpression(AmbassadorToolboxSettings.HexColorPattern, ErrorMessage = "Use a hex color such as #b42318.")]
    [Display(Name = "Banner background color")]
    public string SiteBannerBackgroundColor { get; set; } = AmbassadorToolboxSettings.DefaultSiteBannerBackgroundColor;

    [StringLength(7, MinimumLength = 4)]
    [RegularExpression(AmbassadorToolboxSettings.HexColorPattern, ErrorMessage = "Use a hex color such as #ffffff.")]
    [Display(Name = "Banner text color")]
    public string SiteBannerTextColor { get; set; } = AmbassadorToolboxSettings.DefaultSiteBannerTextColor;

    public bool EnableMerchantReports { get; set; }

    [Required]
    [Display(Name = "Report button text")]
    [StringLength(80)]
    public string ReportButtonText { get; set; } = "Report this merchant";

    public List<MerchantReport> Reports { get; set; } = [];
}

public class MerchantReportViewModel
{
    public string InvoiceId { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? OrderId { get; set; }

    [Required]
    [StringLength(80)]
    public string Reason { get; set; } = "Suspected scam";

    [Required]
    [StringLength(2000)]
    [Display(Name = "What happened?")]
    public string Details { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Contact")]
    public string? Contact { get; set; }

    // Honeypot for the public report form. Real users should never fill this.
    [StringLength(200)]
    public string? Website { get; set; }
}

public class MerchantReportSubmittedViewModel
{
    public string StoreName { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
}
