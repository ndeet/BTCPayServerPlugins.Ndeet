using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public class TipcardsPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.4" }
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("store-integrations-nav", "TipcardsNav");
        base.Execute(applicationBuilder);
    }
}
