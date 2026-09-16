using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Test-only convenience for fixtures whose subject is not Cell creation. Production entry has no
/// seedless overload: it must publish the complete seed produced by <see cref="IDungeonCellFactory"/>.
/// </summary>
internal static class DungeonRunTestEntryExtensions
{
    public static DungeonRunState Begin(
        this IDungeonRunDomainService runs,
        CharacterId characterId,
        DungeonLayout layout,
        byte difficulty) => runs.BeginParty(
            [new DungeonRunParticipant(
                new AccountId(0), characterId, Slot: 0, IsLeader: true)],
            layout,
            difficulty);

    public static DungeonRunState BeginParty(
        this IDungeonRunDomainService runs,
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        byte difficulty) => runs.PreparePartyEntry(participants, layout, difficulty).Run;

    public static DungeonEntryChange PreparePartyEntry(
        this IDungeonRunDomainService runs,
        IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout,
        byte difficulty) => runs.PreparePartyEntry(
            participants,
            layout,
            difficulty,
            DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow);
}
