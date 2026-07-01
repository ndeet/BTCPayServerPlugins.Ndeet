#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxBannerFilter(SettingsRepository settingsRepository) : IAsyncResultFilter
{
    internal const string BannerMarker = "data-ambassador-toolbox-site-banner=\"true\"";
    private static readonly Regex StartTagRegex = new(
        @"<(?<name>[a-z][a-z0-9:-]*)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IdAttributeRegex = new(
        @"\bid\s*=\s*([""'])(?<value>.*?)\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex ClassAttributeRegex = new(
        @"\bclass\s*=\s*([""'])(?<value>.*?)\1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

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

    internal static string InjectBanner(string html, AmbassadorToolboxSettings settings)
    {
        if (html.Contains(BannerMarker, StringComparison.OrdinalIgnoreCase))
            return html;

        var bannerHtml = BuildBannerHtml(settings);
        var insertionIndex = FindInsertionIndex(html);
        return insertionIndex >= 0
            ? html.Insert(insertionIndex, bannerHtml)
            : html;
    }

    private static int FindInsertionIndex(string html)
    {
        var globalNavIndex = FindOpeningTagEnd(html, (name, tag) =>
            string.Equals(name, "div", StringComparison.OrdinalIgnoreCase) &&
            AttributeEquals(tag, IdAttributeRegex, "globalNav"));
        if (globalNavIndex >= 0)
            return globalNavIndex;

        var contentWrapperIndex = FindOpeningTagEnd(html, (name, tag) =>
            IsContentContainerTag(name) &&
            AttributeContainsClass(tag, "content-wrapper"));
        if (contentWrapperIndex >= 0)
            return contentWrapperIndex;

        return -1;
    }

    private static int FindOpeningTagEnd(string html, Func<string, string, bool> predicate)
    {
        foreach (Match match in StartTagRegex.Matches(html))
        {
            var name = match.Groups["name"].Value;
            if (predicate(name, match.Value))
                return match.Index + match.Length;
        }

        return -1;
    }

    private static bool IsContentContainerTag(string name)
    {
        return string.Equals(name, "main", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "section", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "div", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AttributeEquals(string tag, Regex attributeRegex, string expectedValue)
    {
        var match = attributeRegex.Match(tag);
        return match.Success &&
               string.Equals(match.Groups["value"].Value, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AttributeContainsClass(string tag, string expectedClass)
    {
        var match = ClassAttributeRegex.Match(tag);
        return match.Success &&
               match.Groups["value"].Value
                   .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                   .Contains(expectedClass, StringComparer.OrdinalIgnoreCase);
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

        return $$"""
                <style>
                    :root {
                        --ambassador-toolbox-site-banner-height: 0px;
                        --ambassador-toolbox-site-banner-gap: calc(var(--btcpay-space-s) + 5px);
                    }

                    #globalNav {
                        align-items: stretch;
                        flex-wrap: wrap;
                        row-gap: var(--ambassador-toolbox-site-banner-gap);
                    }

                    #globalNav > .ambassador-toolbox-site-banner {
                        order: -100;
                        flex: 0 0 100%;
                        min-width: 0;
                        margin: 0;
                    }

                    @media (max-width: 991px) {
                        #mainMenu {
                            height: calc(var(--mobile-header-height) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                        }

                        #mainMenuHead {
                            position: relative;
                            padding-top: calc(var(--ambassador-toolbox-site-banner-gap) + var(--ambassador-toolbox-site-banner-height));
                        }

                        #globalNav {
                            position: static;
                            align-items: center;
                            flex-wrap: nowrap;
                            row-gap: 0;
                        }

                        #globalNav > .ambassador-toolbox-site-banner {
                            position: absolute;
                            top: 0;
                            left: 0;
                            right: 0;
                            z-index: 1;
                        }

                        #globalNav.globalSearch-mobile-open .globalSearch-shell {
                            top: calc(var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                            height: calc(100vh - var(--ambassador-toolbox-site-banner-height) - var(--ambassador-toolbox-site-banner-gap));
                            height: calc(100dvh - var(--ambassador-toolbox-site-banner-height) - var(--ambassador-toolbox-site-banner-gap));
                        }

                        #mainNav {
                            top: calc(var(--mobile-header-height) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                            height: calc(100vh - var(--mobile-header-height) - var(--ambassador-toolbox-site-banner-height) - var(--ambassador-toolbox-site-banner-gap));
                            height: calc(100dvh - var(--mobile-header-height) - var(--ambassador-toolbox-site-banner-height) - var(--ambassador-toolbox-site-banner-gap));
                            padding-bottom: calc(var(--mobile-header-height) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                        }

                        #mainContent > section {
                            padding-top: calc(var(--content-padding-top) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                        }
                    }

                    .content-wrapper > .ambassador-toolbox-site-banner {
                        margin-bottom: var(--btcpay-space-l);
                    }

                    @media (min-width: 992px) {
                        #globalNav {
                            min-height: calc(var(--global-nav-height) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                        }

                        #globalNav > .ambassador-toolbox-site-banner {
                            flex-basis: calc(100% + (2 * var(--content-padding-horizontal)));
                            margin: calc(var(--btcpay-space-s) * -1) calc(var(--content-padding-horizontal) * -1) 0;
                        }

                        #mainContent > section {
                            padding-top: calc(var(--content-padding-top) + var(--ambassador-toolbox-site-banner-height) + var(--ambassador-toolbox-site-banner-gap));
                        }
                    }
                </style>
                <div class="ambassador-toolbox-site-banner alert alert-danger d-flex flex-wrap align-items-center justify-content-center gap-2 flex-shrink-0 mb-0 rounded-0 border-0 text-center fw-semibold w-100 px-3 py-2" {{BannerMarker}} role="alert" style="background-color:{{backgroundColor}};color:{{textColor}};">
                    <span class="text-break">{{bannerText}}</span>
                </div>
                <script>
                    (() => {
                        const banner = document.querySelector('#globalNav > .ambassador-toolbox-site-banner');
                        if (!banner) return;

                        const setBannerHeight = () => {
                            document.documentElement.style.setProperty(
                                '--ambassador-toolbox-site-banner-height',
                                `${banner.offsetHeight}px`);
                        };

                        setBannerHeight();
                        window.addEventListener('resize', setBannerHeight, { passive: true });
                        if (window.ResizeObserver) {
                            banner.ambassadorToolboxResizeObserver = new ResizeObserver(setBannerHeight);
                            banner.ambassadorToolboxResizeObserver.observe(banner);
                        }
                    })();
                </script>
                """;
    }

    private static async Task WriteHtml(HttpResponse response, Stream body, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength = bytes.Length;
        await body.WriteAsync(bytes);
    }
}
