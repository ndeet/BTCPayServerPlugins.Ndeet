using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Ndeet.Plugins.AmbassadorToolbox;

public class AmbassadorToolboxPlugin : BaseBTCPayServerPlugin
{
    public const string SettingsKey = "NdeetAmbassadorToolboxSettings";

    public override string Identifier => "BTCPayServer.Ndeet.Plugins.AmbassadorToolbox";
    public override string Name => "Ambassador Toolbox";
    public override string Description => "Tools for BTCPay Server ambassadors managing hosted merchants.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.4.0" }
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("server-nav", "/Views/Shared/AmbassadorToolboxNav.cshtml");
        applicationBuilder.AddUIExtension("global-nav", "/Views/Shared/AmbassadorToolboxSiteBanner.cshtml");
        applicationBuilder.AddUIExtension("checkout-end", "/Views/Shared/AmbassadorToolboxCheckoutBanner.cshtml");
        applicationBuilder.AddUIExtension("checkout-noscript-end", "/Views/Shared/AmbassadorToolboxCheckoutBanner.cshtml");
        applicationBuilder.AddScoped<AmbassadorToolboxBannerFilter>();
        applicationBuilder.Configure<MvcOptions>(options =>
            options.Filters.AddService<AmbassadorToolboxBannerFilter>());
        applicationBuilder.AddSingleton<INotificationHandler, MerchantReportNotification.Handler>();
        base.Execute(applicationBuilder);
    }
}
