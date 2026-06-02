using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
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
        applicationBuilder.AddUIExtension("server-nav", "AmbassadorToolboxNav");
        applicationBuilder.AddUIExtension("checkout-end", "AmbassadorToolboxCheckout");
        base.Execute(applicationBuilder);
    }
}
