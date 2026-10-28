using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Test-only convenience for fixtures whose subject is not Cell creation. Production entry has no
/// seedless overload: it must publish the complete seed produced by <see cref="IDungeonCellFactory"/>.
/// A run service built by <c>TestParties.Runs</c> gets the roster's real party formed here when the
/// fixture has not formed one, because a multi-member entry must match a real party.
/// </summary>
internal static class DungeonRunTestEntryExtensions
{
    public static DungeonRunState Begin(
        this IDungeonRunDomainService runs,
        CharacterId characterId,
        DungeonLayout layout,
        DungeonDifficulty difficulty) => runs.BeginParty(
            [new DungeonRunParticipant(
                new AccountId(0), characterId, Slot: 0, IsLeader: true)],
            layout,
            difficulty);

    public static DungeonRunState BeginParty(
        this IDungeonRunDomainService runs,
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty) => runs.PreparePartyEntry(participants, layout, difficulty).Run;

    public static DungeonEntryChange PreparePartyEntry(
        this IDungeonRunDomainService runs,
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        DungeonDifficulty difficulty)
    {
        TestParties.EnsureEntryParty(runs, participants);
        return runs.PreparePartyEntry(
            participants,
            layout,
            difficulty,
            DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow);
    }
}
