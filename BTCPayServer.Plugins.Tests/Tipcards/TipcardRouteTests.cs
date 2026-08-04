using System.Reflection;
using BTCPayServer.Ndeet.Plugins.Tipcards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BTCPayServer.Plugins.Tests.Tipcards;

public class TipcardRouteTests
{
    [Theory]
    [InlineData(nameof(TipcardsController.ClaimCard), "~/plugins/tipcards/claim/{storeId}/{claimId}")]
    [InlineData(nameof(TipcardsController.WithdrawCard), "~/plugins/tipcards/withdraw/{storeId}/{claimId}")]
    public void PublicCardActions_UseStableCardRoutes(string actionName, string expectedTemplate)
    {
        var action = typeof(TipcardsController).GetMethod(actionName);

        Assert.NotNull(action);
        Assert.Equal(expectedTemplate, action.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.NotNull(action.GetCustomAttribute<AllowAnonymousAttribute>());

        var responseCache = action.GetCustomAttribute<ResponseCacheAttribute>();
        Assert.NotNull(responseCache);
        Assert.True(responseCache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, responseCache.Location);
    }

    [Fact]
    public void Controller_HasNoPullPaymentIdClaimRoute()
    {
        var routeTemplates = typeof(TipcardsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
            .Select(attribute => attribute.Template)
            .Where(template => template != null);

        Assert.DoesNotContain("~/plugins/tipcards/claim/{pullPaymentId}", routeTemplates);
    }
}
