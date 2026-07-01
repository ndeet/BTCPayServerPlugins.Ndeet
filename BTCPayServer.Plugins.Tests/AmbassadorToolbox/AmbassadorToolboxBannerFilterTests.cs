using System;
using BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;
using Xunit;

namespace BTCPayServer.Plugins.Tests.AmbassadorToolbox;

public class AmbassadorToolboxBannerFilterTests
{
    private static readonly AmbassadorToolboxSettings Settings = new()
    {
        SiteBannerText = "Visible warning",
        SiteBannerBackgroundColor = "#112233",
        SiteBannerTextColor = "#ffffff"
    };

    [Fact]
    public void InjectBanner_InsertsInsideGlobalNavForCurrentBackendLayout()
    {
        const string html =
            """<body><header id="mainMenu"><div id="mainMenuHead"><button id="mainMenuToggle">Menu</button><div id="globalNav"><button id="globalSearchMobileToggle">Search</button><div id="globalSearchShell"></div><div id="mainNavSettings"></div></div></div></header><main id="mainContent"><section>Backend</section></main></body>""";

        var result = AmbassadorToolboxBannerFilter.InjectBanner(html, Settings);

        AssertSingleBanner(result);
        Assert.True(result.IndexOf(AmbassadorToolboxBannerFilter.BannerMarker, StringComparison.Ordinal) >
                    result.IndexOf("id=\"globalNav\"", StringComparison.Ordinal));
        Assert.True(result.IndexOf(AmbassadorToolboxBannerFilter.BannerMarker, StringComparison.Ordinal) <
                    result.IndexOf("id=\"globalSearchMobileToggle\"", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectBanner_InsertsInsideSignedOutContentWrapperWhenMainContentIsMissing()
    {
        const string html =
            """<body class="d-flex flex-column min-vh-100"><section class="content-wrapper flex-grow-1"><div class="container">Login</div></section></body>""";

        var result = AmbassadorToolboxBannerFilter.InjectBanner(html, Settings);

        AssertSingleBanner(result);
        Assert.True(result.IndexOf(AmbassadorToolboxBannerFilter.BannerMarker, StringComparison.Ordinal) >
                    result.IndexOf("content-wrapper flex-grow-1", StringComparison.Ordinal));
        Assert.True(result.IndexOf(AmbassadorToolboxBannerFilter.BannerMarker, StringComparison.Ordinal) <
                    result.IndexOf("""<div class="container">Login</div>""", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectBanner_DoesNotDuplicateExistingBanner()
    {
        var html = $"""<body><main id="mainContent"><div {AmbassadorToolboxBannerFilter.BannerMarker}>Already here</div></main></body>""";

        var result = AmbassadorToolboxBannerFilter.InjectBanner(html, Settings);

        Assert.Equal(html, result);
        Assert.Equal(1, CountOccurrences(result, AmbassadorToolboxBannerFilter.BannerMarker));
    }

    [Fact]
    public void InjectBanner_LeavesUnknownLayoutsUnchanged()
    {
        const string html = """<body class="custom-layout"><div>Content</div></body>""";

        var result = AmbassadorToolboxBannerFilter.InjectBanner(html, Settings);

        Assert.Equal(html, result);
    }

    private static void AssertSingleBanner(string html)
    {
        Assert.Equal(1, CountOccurrences(html, AmbassadorToolboxBannerFilter.BannerMarker));
        Assert.Contains("Visible warning", html);
        Assert.Contains("background-color:#112233", html);
        Assert.Contains("color:#ffffff", html);
        Assert.Contains("d-flex flex-wrap align-items-center justify-content-center gap-2 flex-shrink-0", html);
        Assert.Contains("#globalNav > .ambassador-toolbox-site-banner", html);
        Assert.Contains("--ambassador-toolbox-site-banner-height", html);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
