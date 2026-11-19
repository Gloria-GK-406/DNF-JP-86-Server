using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidRewardSettlementSeed(Guid Instance, uint Number, byte Phase, bool Succeeded,
    int DeathCount, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
    IReadOnlyList<CharacterId> Audience, IReadOnlyList<AntonRaidRewardParticipant> Eligible);

public enum AntonRaidRewardStage
{
    Result,
    Cards,
    SquadDelay,
    SquadItems,
    Delivering,
    Complete,
}

public sealed record AntonRaidRewardCard(CharacterId Character, AntonRaidRewardKind Kind, byte Slot, byte Flags,
    AntonRaidRewardItem Item);

public sealed record AntonRaidRewardSnapshot(AntonRaidRewardSettlementSeed Seed, byte Rank, AntonRaidRewardStage Stage,
    IReadOnlyList<AntonRaidRewardCard> Revealed);

public enum AntonRaidRewardAdvanceKind { MoviePrompt, CardsOpened, CardsAutoSelected, SquadItemsShown, Deliver }

public sealed record AntonRaidRewardAdvance(AntonRaidRewardAdvanceKind Kind, AntonRaidRewardSnapshot Snapshot,
    IReadOnlyList<AntonRaidRewardCard> Revealed, IReadOnlyList<AntonRaidRewardGrant> Grants);

public sealed record AntonRaidMovieResult(AntonRaidRewardSnapshot Snapshot, bool OpenedNow);

public sealed record AntonRaidRewardSelection(bool Accepted, AntonRaidRewardSnapshot? Snapshot = null,
    AntonRaidRewardCard? Reveal = null, AntonRaidRewardGrant? Grant = null);

public readonly record struct AntonRaidRewardDeadline(Guid Instance, byte Phase, DateTimeOffset At);

public interface IAntonRaidRewardSettlementDomainService
{
    AntonRaidRewardSnapshot? Begin(AntonRaidRewardSettlementSeed seed, DateTimeOffset now);
    AntonRaidRewardSnapshot? Find(Guid instance, byte phase);
    AntonRaidMovieResult? FinishMovie(Guid instance, byte phase, CharacterId character, byte flag, DateTimeOffset now);
    AntonRaidRewardSelection Select(Guid instance, byte phase, CharacterId character, byte type, byte slot, DateTimeOffset now);
    IReadOnlyList<AntonRaidRewardDeadline> Deadlines();
    AntonRaidRewardAdvance? Advance(AntonRaidRewardDeadline expected, DateTimeOffset now);
    bool Complete(Guid instance, byte phase);
    void Forget(Guid instance);
}
