using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Services.Notifications;
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
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.4" }
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("server-nav", "/Views/Shared/AmbassadorToolboxNav.cshtml");
        applicationBuilder.AddUIExtension("layout-banner", "/Views/Shared/AmbassadorToolboxSiteBanner.cshtml");
        applicationBuilder.AddUIExtension("checkout-end", "/Views/Shared/AmbassadorToolboxCheckoutBanner.cshtml");
        applicationBuilder.AddUIExtension("checkout-noscript-end", "/Views/Shared/AmbassadorToolboxCheckoutBanner.cshtml");
        applicationBuilder.AddSingleton<INotificationHandler, MerchantReportNotification.Handler>();
        base.Execute(applicationBuilder);
    }
}
