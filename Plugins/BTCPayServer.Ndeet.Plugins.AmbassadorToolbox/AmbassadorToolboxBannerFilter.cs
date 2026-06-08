#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxBannerFilter(SettingsRepository settingsRepository) : IAsyncResultFilter
{
    private const string BannerMarker = "data-ambassador-toolbox-site-banner=\"true\"";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is not ViewResult || IsCheckoutPath(context.HttpContext.Request.Path))
        {
            await next();
            return;
        }

        var response = context.HttpContext.Response;
        var originalBody = response.Body;
        await using var buffer = new MemoryStream();
        response.Body = buffer;

        ResultExecutedContext executedContext;
        try
        {
            executedContext = await next();
        }
        finally
        {
            response.Body = originalBody;
        }

        buffer.Position = 0;

        if (executedContext.Exception is not null && !executedContext.ExceptionHandled)
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        if (!IsHtmlResponse(response))
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        var html = Encoding.UTF8.GetString(buffer.ToArray());
        if (html.Contains(BannerMarker, StringComparison.OrdinalIgnoreCase))
        {
            await WriteHtml(response, originalBody, html);
            return;
        }

        var settings = await settingsRepository.GetSettingAsync<AmbassadorToolboxSettings>(AmbassadorToolboxPlugin.SettingsKey)
                       ?? new AmbassadorToolboxSettings();
        if (!settings.EnableSiteBanner)
        {
            await WriteHtml(response, originalBody, html);
            return;
        }

        await WriteHtml(response, originalBody, InjectBanner(html, settings));
    }

    private static bool IsCheckoutPath(PathString path)
    {
        return path.StartsWithSegments("/i") ||
               path.StartsWithSegments("/invoice") ||
               path.StartsWithSegments("/plugins/ambassador-toolbox/report");
    }

    private static bool IsHtmlResponse(HttpResponse response)
    {
        return response.StatusCode == StatusCodes.Status200OK &&
               (string.IsNullOrEmpty(response.ContentType) ||
                response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase));
    }

    private static string InjectBanner(string html, AmbassadorToolboxSettings settings)
    {
        var bannerHtml = BuildBannerHtml(settings);
        var mainContentIndex = html.IndexOf("<main id=\"mainContent\"", StringComparison.OrdinalIgnoreCase);
        if (mainContentIndex >= 0)
        {
            var mainContentEnd = html.IndexOf('>', mainContentIndex);
            if (mainContentEnd >= 0)
                return html.Insert(mainContentEnd + 1, bannerHtml);
        }

        var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex < 0)
            return bannerHtml + html;

        var bodyEnd = html.IndexOf('>', bodyIndex);
        return bodyEnd < 0
            ? bannerHtml + html
            : html.Insert(bodyEnd + 1, bannerHtml);
    }

    private static string BuildBannerHtml(AmbassadorToolboxSettings settings)
    {
        var bannerText = HtmlEncoder.Default.Encode(
            AmbassadorToolboxSettings.NormalizeSiteBannerText(settings.SiteBannerText));
        var backgroundColor = HtmlEncoder.Default.Encode(AmbassadorToolboxSettings.NormalizeHexColor(
            settings.SiteBannerBackgroundColor,
            AmbassadorToolboxSettings.DefaultSiteBannerBackgroundColor));
        var textColor = HtmlEncoder.Default.Encode(AmbassadorToolboxSettings.NormalizeHexColor(
            settings.SiteBannerTextColor,
            AmbassadorToolboxSettings.DefaultSiteBannerTextColor));

        return $"""
                <div class="ambassador-toolbox-site-banner alert alert-danger mb-0 rounded-0 border-0 text-center fw-bold w-100" {BannerMarker} role="alert" style="background-color:{backgroundColor};color:{textColor};">{bannerText}</div>
                """;
    }

    private static async Task WriteHtml(HttpResponse response, Stream body, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength = bytes.Length;
        await body.WriteAsync(bytes);
    }
}
