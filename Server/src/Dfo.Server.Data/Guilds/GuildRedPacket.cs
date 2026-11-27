using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildRedPacketProduct(int ItemId, int ShopId, int Price, int ContributionRate);

public sealed record GuildRedPacketRules(IReadOnlyDictionary<int, GuildRedPacketProduct> Products,
    int MinimumMembers, int DailyPurchaseLimit, int DailyTakeLimit)
{
    public IReadOnlyDictionary<GuildDailyContributionKind, int> DailyContributionPoints { get; init; }
        = new Dictionary<GuildDailyContributionKind, int>();
    public GuildClearContributionRule? GuildDungeonContribution { get; init; }
    public GuildClearContributionRule? PartyDungeonContribution { get; init; }

    public const int GuildActiveLimit = 500;
    public const int MinShares = 5;
    public const int MaxShares = 30;
    public const int ContributionCoin = 490003262;
}

public sealed record GuildRedPacket(uint Id, GuildId Guild, CharacterId Sender, AccountId SenderAccount,
    string SenderName, int ItemId, int Amount, int ContributionRate, int Shares, int Remaining, int SharesLeft,
    long IssuedAt, long ExpiresAt, bool SenderDeparted, long? SettledAt, int Contribution)
{
    public bool IsOpenAt(long timestamp) => SettledAt is null && SharesLeft > 0 && ExpiresAt > timestamp;
}

public sealed record GuildRedPacketClaim(uint Packet, AccountId Account, CharacterId Character,
    string Name, int Amount, long TakenAt);

public sealed record GuildRedPacketList(int MemberCount, int TakenToday, IReadOnlyList<GuildRedPacket> Packets);


public enum GuildRedPacketStatus { Success, Unavailable, Invalid, Full, DailyTakeLimit, AlreadyTaken, Gone, BalanceLimit }

public sealed record GuildRedPacketResult(GuildRedPacketStatus Status, int Amount = 0, int ItemId = 0,
    BusinessSlot? Slot = null, int RemainingAtSlot = 0, GuildId? Guild = null);
