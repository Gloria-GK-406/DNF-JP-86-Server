using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// CMD 0x0211's request as the client builds it (0x00E46954..0x00E469F3): one conditional summon
/// the party leader asks for after NOTI 0x0138.
/// </summary>
/// <param name="Kind">0 for a monster, 1 for an APC.</param>
/// <param name="RequestId">The client's own running number, echoed so it can pair the answer.</param>
/// <param name="X">The requesting room's maze column.</param>
/// <param name="Y">The requesting room's maze row.</param>
/// <param name="Count">How many identical summons the client merged into this request.</param>
public sealed record DungeonEntranceSummonRequest(
    ushort Kind, int MonsterCode, int RequestId, int MapId, ushort X, ushort Y, byte Count);

/// <summary>One summoned actor: the sequence the client will report it dead by, and what it is.</summary>
/// <param name="Grade">0 ordinary, 1 champion, 2 super champion, 3 boss.</param>
public sealed record DungeonEntranceSummoned(ushort Sequence, int MonsterCode, byte Grade, ushort Level);

/// <param name="Actors">Empty when the request was refused.</param>
public sealed record DungeonEntranceSummonResult(int RequestId, IReadOnlyList<DungeonEntranceSummoned> Actors)
{
    public bool Accepted => this.Actors.Count > 0;
}

/// <summary>
/// The write side of a dungeon's <c>[boss room entrance condition]</c>: calling in the monster its
/// hunted conditional monsters unlock.
/// </summary>
public interface IDungeonEntranceDomainService
{
    /// <summary>
    /// Stages the requested summon into the requester's current room in the ambient transaction; the
    /// result carries no actors when the request is not one this run owes.
    /// </summary>
    ValueTask<DungeonEntranceSummonResult> SummonAsync(SelectedSessionIdentity requester,
        DungeonEntranceSummonRequest request, CancellationToken cancellationToken);
}
