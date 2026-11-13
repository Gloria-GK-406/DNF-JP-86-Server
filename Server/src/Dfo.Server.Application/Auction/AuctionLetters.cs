using System.Globalization;
using Dfo.Server.Abstractions.Auction;

namespace Dfo.Server.Application.Auction;

/// <summary>One kind of letter the auction house writes.</summary>
internal sealed record AuctionLetter(Func<AuctionListing?, int, string> Text)
{
    public string Body(AuctionListing? listing, int amount = 0) => Text(listing, amount);
}

/// <summary>The auction house's letters, after the 70 server's <c>taiwan.xml</c> templates 0-6.</summary>
internal static class AuctionLetters
{
    public const string Title = "拍卖行";

    public static readonly AuctionLetter Outbid = new((_, amount) => $"您的竞拍已被超过，竞拍款 {Gold(amount)} 金币已退回。");
    public static readonly AuctionLetter Bought = new((_, _) => "您在拍卖行购得的物品已送达。");
    public static readonly AuctionLetter ExpiredToSeller = new((listing, _) => listing is { Deposit: > 0 }
        ? $"您的拍卖物品已流拍，物品与押金 {Gold(listing.Deposit)} 金币已退回。"
        : "您的拍卖物品已流拍，已退回。");
    public static readonly AuctionLetter CancelledToSeller = new((_, _) => "您已撤销拍卖，物品已退回，押金不予退还。");
    public static readonly AuctionLetter CancelledToBidder = new((_, amount) => $"卖家已撤销拍卖，您的竞拍款 {Gold(amount)} 金币已退回。");

    public static string Sold(AuctionListing listing, int quantity, long price, long fee, int deposit)
    {
        var lines = quantity < listing.QuantityInitial || deposit == 0
            ? $"您的拍卖物品售出 {quantity} 个。"
            : "您的拍卖物品已成交。";
        lines += $"\n成交价 +{Gold(price)}";
        if (deposit > 0) lines += $"\n押金 +{Gold(deposit)}";
        return lines + $"\n手续费 -{Gold(fee)}\n实得 {Gold(price - fee + deposit)} 金币";
    }

    public static string ConsignmentSold(AuctionListing listing, long price, long fee, long net) =>
        $"您寄售的 {Gold(listing.GoldAmount)} 金币已成交。\n成交价 +{Gold(price)} 代币券\n手续费 -{Gold(fee)} 代币券\n实得 {Gold(net)} 代币券，已直接存入账号。";

    private static string Gold(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
