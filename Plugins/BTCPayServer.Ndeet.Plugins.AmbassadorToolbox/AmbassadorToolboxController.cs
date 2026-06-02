using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Ndeet.Plugins.AmbassadorToolbox.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
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
    StoreRepository storeRepository) : Controller
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

        if (!ModelState.IsValid)
            return View(model);

        var settings = await GetSettings();
        settings.Reports.Add(new MerchantReport
        {
            Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(12)),
            CreatedAt = DateTimeOffset.UtcNow,
            InvoiceId = invoice.Id,
            StoreId = invoice.StoreId,
            StoreName = storeName,
            OrderId = invoice.Metadata?.OrderId,
            Reason = model.Reason.Trim(),
            Details = model.Details.Trim(),
            Contact = string.IsNullOrWhiteSpace(model.Contact) ? null : model.Contact.Trim(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        });
        await SaveSettings(settings);

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

    private static AmbassadorToolboxIndexViewModel ToIndexViewModel(AmbassadorToolboxSettings settings)
    {
        return new AmbassadorToolboxIndexViewModel
        {
            EnableMerchantReports = settings.EnableMerchantReports,
            ReportButtonText = settings.ReportButtonText,
            Reports = settings.Reports
                .OrderByDescending(report => report.CreatedAt)
                .ToList()
        };
    }
}
