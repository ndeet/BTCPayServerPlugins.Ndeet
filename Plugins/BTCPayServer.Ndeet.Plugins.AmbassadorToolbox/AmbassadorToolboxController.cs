using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Ndeet.Plugins.AmbassadorToolbox.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxController(
    SettingsRepository settingsRepository,
    InvoiceRepository invoiceRepository,
    StoreRepository storeRepository,
    NotificationSender notificationSender,
    MerchantReportSubmissionThrottle reportSubmissionThrottle) : Controller
{
    [HttpGet("~/server/ambassador-toolbox")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Index()
    {
        var settings = await GetSettings();
        return View(ToIndexViewModel(settings));
    }

    [HttpPost("~/server/ambassador-toolbox")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Index(AmbassadorToolboxIndexViewModel model)
    {
        var settings = await GetSettings();

        if (!ModelState.IsValid)
        {
            model.Reports = settings.Reports
                .OrderByDescending(report => report.CreatedAt)
                .ToList();
            return View(model);
        }

        settings.EnableSiteBanner = model.EnableSiteBanner;
        settings.SiteBannerText = AmbassadorToolboxSettings.NormalizeSiteBannerText(model.SiteBannerText);
        settings.SiteBannerBackgroundColor = AmbassadorToolboxSettings.NormalizeHexColor(
            model.SiteBannerBackgroundColor,
            AmbassadorToolboxSettings.DefaultSiteBannerBackgroundColor);
        settings.SiteBannerTextColor = AmbassadorToolboxSettings.NormalizeHexColor(
            model.SiteBannerTextColor,
            AmbassadorToolboxSettings.DefaultSiteBannerTextColor);

        settings.EnableMerchantReports = model.EnableMerchantReports;
        settings.ReportButtonText = string.IsNullOrWhiteSpace(model.ReportButtonText)
            ? "Report this merchant"
            : model.ReportButtonText.Trim();

        await SaveSettings(settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Ambassador Toolbox settings updated.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("~/server/ambassador-toolbox/reports/{reportId}/toggle-resolved")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ToggleResolved(string reportId)
    {
        var settings = await GetSettings();
        var report = settings.Reports.FirstOrDefault(r => r.Id == reportId);
        if (report is null)
            return NotFound();

        report.Resolved = !report.Resolved;
        await SaveSettings(settings);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("~/server/ambassador-toolbox/reports/{reportId}/delete")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteReport(string reportId)
    {
        var settings = await GetSettings();
        var removed = settings.Reports.RemoveAll(r => r.Id == reportId);
        if (removed == 0)
            return NotFound();

        await SaveSettings(settings);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("~/plugins/ambassador-toolbox/report/{invoiceId}")]
    public async Task<IActionResult> ReportMerchant(string invoiceId)
    {
        var reportContext = await GetReportContext(invoiceId);
        if (reportContext is null)
            return NotFound();

        var (invoice, storeName) = reportContext.Value;
        return View(new MerchantReportViewModel
        {
            InvoiceId = invoice.Id,
            StoreName = storeName,
            OrderId = invoice.Metadata?.OrderId
        });
    }

    [HttpPost("~/plugins/ambassador-toolbox/report/{invoiceId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportMerchant(string invoiceId, MerchantReportViewModel model)
    {
        var reportContext = await GetReportContext(invoiceId);
        if (reportContext is null)
            return NotFound();

        var (invoice, storeName) = reportContext.Value;
        model.InvoiceId = invoice.Id;
        model.StoreName = storeName;
        model.OrderId = invoice.Metadata?.OrderId;

        if (!string.IsNullOrWhiteSpace(model.Website))
            return View("ReportSubmitted", new MerchantReportSubmittedViewModel
            {
                StoreName = storeName,
                InvoiceId = invoice.Id
            });

        if (!ModelState.IsValid)
            return View(model);

        var now = DateTimeOffset.UtcNow;
        if (!reportSubmissionThrottle.TryConsume(GetReportThrottleKey(), now, out var retryAt))
        {
            SetRetryAfterHeader(now, retryAt);
            ModelState.AddModelError(string.Empty, "Please wait a minute before submitting another report.");
            return View(model);
        }

        var settings = await GetSettings();
        var report = new MerchantReport
        {
            Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(12)),
            CreatedAt = now,
            InvoiceId = invoice.Id,
            StoreId = invoice.StoreId,
            StoreName = storeName,
            OrderId = invoice.Metadata?.OrderId,
            Reason = model.Reason.Trim(),
            Details = model.Details.Trim(),
            Contact = string.IsNullOrWhiteSpace(model.Contact) ? null : model.Contact.Trim(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        };
        settings.Reports.Add(report);
        await SaveSettings(settings);
        await notificationSender.SendNotification(new AdminScope(), new MerchantReportNotification(report));

        return View("ReportSubmitted", new MerchantReportSubmittedViewModel
        {
            StoreName = storeName,
            InvoiceId = invoice.Id
        });
    }

    private async Task<(InvoiceEntity invoice, string storeName)?> GetReportContext(string invoiceId)
    {
        var settings = await GetSettings();
        if (!settings.EnableMerchantReports)
            return null;

        var invoice = (await invoiceRepository.GetInvoices(new InvoiceQuery
        {
            InvoiceId = [invoiceId],
            IncludeArchived = true
        })).FirstOrDefault();
        if (invoice is null)
            return null;

        var store = await storeRepository.FindStore(invoice.StoreId);
        return (invoice, store?.StoreName ?? invoice.StoreId);
    }

    private async Task<AmbassadorToolboxSettings> GetSettings()
    {
        var settings = await settingsRepository.GetSettingAsync<AmbassadorToolboxSettings>(AmbassadorToolboxPlugin.SettingsKey)
                       ?? new AmbassadorToolboxSettings();
        settings.SiteBannerText = AmbassadorToolboxSettings.NormalizeSiteBannerText(settings.SiteBannerText);
        settings.SiteBannerBackgroundColor = AmbassadorToolboxSettings.NormalizeHexColor(
            settings.SiteBannerBackgroundColor,
            AmbassadorToolboxSettings.DefaultSiteBannerBackgroundColor);
        settings.SiteBannerTextColor = AmbassadorToolboxSettings.NormalizeHexColor(
            settings.SiteBannerTextColor,
            AmbassadorToolboxSettings.DefaultSiteBannerTextColor);
        settings.ReportButtonText = string.IsNullOrWhiteSpace(settings.ReportButtonText)
            ? "Report this merchant"
            : settings.ReportButtonText;
        settings.Reports ??= [];
        return settings;
    }

    private Task SaveSettings(AmbassadorToolboxSettings settings)
    {
        return settingsRepository.UpdateSetting(settings, AmbassadorToolboxPlugin.SettingsKey);
    }

    private string GetReportThrottleKey()
    {
        var remoteIpAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress is null)
            return "unknown";

        return remoteIpAddress.IsIPv4MappedToIPv6
            ? remoteIpAddress.MapToIPv4().ToString()
            : remoteIpAddress.ToString();
    }

    private void SetRetryAfterHeader(DateTimeOffset now, DateTimeOffset retryAt)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((retryAt - now).TotalSeconds));
        Response.StatusCode = 429;
        Response.Headers["Retry-After"] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private static AmbassadorToolboxIndexViewModel ToIndexViewModel(AmbassadorToolboxSettings settings)
    {
        return new AmbassadorToolboxIndexViewModel
        {
            EnableSiteBanner = settings.EnableSiteBanner,
            SiteBannerText = settings.SiteBannerText,
            SiteBannerBackgroundColor = settings.SiteBannerBackgroundColor,
            SiteBannerTextColor = settings.SiteBannerTextColor,
            EnableMerchantReports = settings.EnableMerchantReports,
            ReportButtonText = settings.ReportButtonText,
            Reports = settings.Reports
                .OrderByDescending(report => report.CreatedAt)
                .ToList()
        };
    }
}
