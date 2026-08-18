using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// A dungeon run in progress: the maze it was given, and which of its rooms the character is in.
/// </summary>
/// <param name="VisitedCells">
/// The cells entered so far. A room the character has been in before is announced as a revisit,
/// which the client distinguishes from a first entry.
/// </param>
public sealed record DungeonRunState(
    DungeonLayout Layout,
    MazeCell CurrentCell,
    int CurrentRoomMapId,
    IReadOnlySet<MazeCell> VisitedCells);

/// <summary>The dungeon runs the server currently has going, and who is in them.</summary>
/// <remarks>
/// Deliberately small, and deliberately not the aggregate described in
/// <c>Docs/领域结构重构设计.md</c>. It exists because walking between rooms cannot be answered
/// without remembering which maze the character was given, and that remembering had to start
/// somewhere. It keeps only what the next frame needs.
/// <para>
/// In memory only. There is no reconnect in this game -- a character who drops in a dungeon
/// reappears in town -- so a run has nothing to survive and nothing to restore.
/// </para>
/// <para>
/// Keyed by character rather than by party because parties are not modelled yet. When they are,
/// entry becomes a party-grained transition and this key moves with it.
/// </para>
/// </remarks>
public interface IDungeonRunDomainService
{
    /// <summary>
    /// Starts a run in its layout's start room, replacing whatever that character was in.
    /// </summary>
    /// <remarks>
    /// Replacing rather than refusing: a character who selects a dungeon has left whatever they
    /// were in, and the client has already been told so. Refusing here would leave the server
    /// describing a run the client has abandoned.
    /// </remarks>
    DungeonRunState Begin(CharacterId characterId, DungeonLayout layout);

    /// <summary>The run this character is in, or null when they are not in one.</summary>
    DungeonRunState? Find(CharacterId characterId);

    /// <summary>
    /// Moves the character into an adjoining cell, or answers null when the maze declares no room
    /// there.
    /// </summary>
    /// <remarks>
    /// A cell outside the maze is refused rather than clamped, which is what the reference server
    /// does with a move it cannot resolve. The client asks for a cell it can see a door to, so a
    /// refusal means the two disagree about the maze, and moving somewhere else would hide that.
    /// </remarks>
    DungeonRunState? TryMove(CharacterId characterId, MazeCell target);

    /// <summary>Ends the run. False when the character was not in one.</summary>
    bool End(CharacterId characterId);
}
