#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox.ViewModels;

public class AmbassadorToolboxIndexViewModel
{
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
}

public class MerchantReportSubmittedViewModel
{
    public string StoreName { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
}
