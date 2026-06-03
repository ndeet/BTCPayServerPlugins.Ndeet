#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxSettings
{
    public const string DefaultSiteBannerText = "Demo instance only. Not for commercial or production use.";
    public const string DefaultSiteBannerBackgroundColor = "#b42318";
    public const string DefaultSiteBannerTextColor = "#ffffff";
    public const string HexColorPattern = "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$";

    public bool EnableSiteBanner { get; set; }
    public string SiteBannerText { get; set; } = DefaultSiteBannerText;
    public string SiteBannerBackgroundColor { get; set; } = DefaultSiteBannerBackgroundColor;
    public string SiteBannerTextColor { get; set; } = DefaultSiteBannerTextColor;

    public bool EnableMerchantReports { get; set; }
    public string ReportButtonText { get; set; } = "Report this merchant";
    public List<MerchantReport> Reports { get; set; } = [];

    public static string NormalizeSiteBannerText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? DefaultSiteBannerText : text.Trim();
    }

    public static string NormalizeHexColor(string? color, string fallback)
    {
        var value = color?.Trim();
        return value is not null && Regex.IsMatch(value, HexColorPattern) ? value : fallback;
    }
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
