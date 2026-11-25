using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

public enum PvpColosseumKind : byte { Individual = 0, TotalMatch = 7 }

public sealed record PvpColosseumPrize(int ItemId, int Count, int DisplayType);

public sealed record PvpColosseumRewardOption(int Group, int Weight, PvpColosseumPrize Prize);

public sealed record PvpColosseumRules(int EntranceItem, int EntranceCost, int DailyLimit, int MaxWins, int MaxLosses,
    int MinimumRp, IReadOnlyDictionary<int, IReadOnlyList<PvpColosseumRewardOption>> Rewards);

public interface IPvpColosseumRuleSource
{
    PvpColosseumRules Load();
}

public readonly record struct PvpColosseumOwner(PvpColosseumKind Kind, int Id)
{
    public static PvpColosseumOwner Of(PvpColosseumKind kind, CharacterId character, AccountId account) =>
        new(kind, kind == PvpColosseumKind.TotalMatch ? account.Value : character.Value);
}

public sealed record PvpColosseumRun(Guid Id, PvpColosseumOwner Owner, CharacterId Recipient, DateOnly EntryDay,
    int Wins, int Losses, ImmutableArray<PvpColosseumPrize> Prizes)
{
    public bool Complete => !Prizes.IsDefaultOrEmpty;
}

public interface IPvpColosseumStore
{
    ValueTask<PvpColosseumRun?> ReadAsync(PvpColosseumOwner owner, CancellationToken ct);
    ValueTask<int> EntriesAsync(PvpColosseumOwner owner, DateOnly day, CancellationToken ct);
    ValueTask InsertAsync(PvpColosseumRun run, CancellationToken ct);
    ValueTask UpdateAsync(PvpColosseumRun run, CancellationToken ct);
    ValueTask CloseAsync(Guid run, CancellationToken ct);
}

public sealed record PvpColosseumEntry(PvpColosseumRun Run, int ItemId, int Price, int? Balance);
public sealed record PvpColosseumLobbyState(PvpColosseumRun? Individual, PvpColosseumRun? TotalMatch, int IndividualEntries,
    int TotalMatchEntries);
public sealed record PvpColosseumDelivery(int? VictoryEmblemBalance, ItemDeliveryReceipt Items);

public interface IPvpColosseumDomainService
{
    ValueTask<PvpColosseumLobbyState> ReadLobbyAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<PvpColosseumEntry?> EnterAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel, PvpColosseumKind kind,
        CancellationToken ct);
    ValueTask<PvpColosseumDelivery?> CollectAsync(SelectedSessionIdentity actor, ChannelEntryIdentity channel, CancellationToken ct);
}
