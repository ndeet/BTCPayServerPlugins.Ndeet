# BTCPayServerPlugins.Ndeet

My plugin repository for [BTCPay Server](https://github.com/btcpayserver/btcpayserver).

Note: All plugins in this repo are vibe coded. Handle with care ;)

Forked from great multi plugin repository from RockstarDev: https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev

## Plugins

| Plugin | Description                                                                                                                                                |
|--------|------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Tipcards** | Create sets of tipcards with QR codes backed by Lightning LNURL-withdraw. Print them, hand them out, people scan and claim sats. Shout out to tipcards.io  |
| **Ambassador Toolbox** | Tools for BTCPay ambassadors hosting merchants, starting with checkout merchant reports for scam and abuse review. |

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) (for running tests)
- Git with submodule support

### Clone and Build

```bash
git clone --recurse-submodules <repo-url>
cd BTCPayServerPlugins.Ndeet
dotnet build
```

### Configure for Local Development

Run ConfigBuilder to generate `appsettings.dev.json` with all plugin DLL paths:

```bash
dotnet run --project ConfigBuilder
```

This writes `submodules/btcpayserver/BTCPayServer/appsettings.dev.json` with the `DEBUG_PLUGINS` value pointing to every plugin's built DLL.

### Run BTCPay Server with Plugins

```bash
cd submodules/btcpayserver
dotnet run --project BTCPayServer
```

### Run Tests

```bash
docker compose -f submodules/btcpayserver/BTCPayServer.Tests/docker-compose.yml up -d dev
dotnet test BTCPayServer.Plugins.Tests --filter "Category=PlaywrightUITest"
docker compose -f submodules/btcpayserver/BTCPayServer.Tests/docker-compose.yml down --volumes
```

## License

[MIT](LICENSE)
