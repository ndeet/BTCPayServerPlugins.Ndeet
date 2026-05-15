using System;
using System.Collections.Generic;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public class TipcardsStoreSettings
{
    public List<TipcardSetData> Sets { get; set; } = new();
    public bool ShowWalletRecommendations { get; set; } = true;
    public List<WalletRecommendation> WalletRecommendations { get; set; }

    public static List<WalletRecommendation> DefaultWalletRecommendations => new()
    {
        new() { Name = "Blitz Wallet", Url = "https://blitzwalletapp.com", Description = "Simple and fast" },
        new() { Name = "Wallet of Satoshi", Url = "https://www.walletofsatoshi.com", Description = "Easiest to get started" },
        new() { Name = "Aqua Wallet", Url = "https://aquawallet.io", Description = "Bitcoin & Lightning" },
        new() { Name = "Phoenix", Url = "https://phoenix.acinq.co", Description = "Self-custodial Lightning" }
    };
}

public class TipcardSetData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public long SatsPerCard { get; set; }
    public int NumberOfCards { get; set; }
    public List<string> PullPaymentIds { get; set; } = new();
    public DateTimeOffset CreatedDate { get; set; }

    public string CardHeadline { get; set; } = "You received a tip!";
    public string CardText { get; set; } = "Scan this QR code with a Lightning wallet to claim your sats.";
    public QrLogoType QrLogo { get; set; } = QrLogoType.Bitcoin;
}

public enum QrLogoType
{
    Bitcoin,
    Lightning,
    None
}

public class WalletRecommendation
{
    public string Name { get; set; }
    public string Url { get; set; }
    public string Description { get; set; }
}
