#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxSettings
{
    public bool EnableMerchantReports { get; set; }
    public string ReportButtonText { get; set; } = "Report this merchant";
    public List<MerchantReport> Reports { get; set; } = [];
}

public class MerchantReport
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string InvoiceId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Resolved { get; set; }
}
