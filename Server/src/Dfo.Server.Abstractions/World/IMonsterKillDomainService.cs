namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What a reported kill turns out to have been.
/// </summary>
/// <param name="Level">
/// The level the client was told, which is the dungeon's basis level wherever the map declined to
/// set one -- the same substitution the room frame made.
/// </param>
/// <param name="Rank">The word the map closes the monster's record with.</param>
/// <param name="IsMonster">
/// False for an AI character. They stand in the room and are numbered with it, but they fight
/// beside the player rather than against them, so their death pays nothing and drops nothing.
/// </param>
public readonly record struct IdentifiedKill(
    int Code, int Level, MonsterRank Rank, bool IsMonster);

/// <summary>
/// Which of a room's declared actors a reported kill names, and how many it declares.
/// </summary>
/// <param name="Kill">
/// Null when the running number names none of them. That means the client and this server disagree
/// about what is in the room, and paying an invented monster would turn that disagreement into
/// experience and items.
/// </param>
/// <param name="DeclaredCount">
/// Carried alongside because a caller that has to say why it refused needs to say how many there
/// were.
/// </param>
public readonly record struct KillIdentification(IdentifiedKill? Kill, int DeclaredCount);

/// <summary>
/// What a kill reported against a run means.
/// </summary>
/// <remarks>
/// The current Cell already owns the actor and its frozen monster classification. Identification
/// and clear checks consume that snapshot only; runtime kill handling does not reread room content,
/// experience rules or loot rules.
/// </remarks>
public interface IMonsterKillDomainService
{
    /// <summary>
    /// Which declared actor this running number names, and whether its death pays.
    /// </summary>
    /// <remarks>
    /// Answered once and used twice by its caller, because experience and drops are decided from
    /// the same three facts -- what it was, what level, what rank -- and looking it up separately
    /// for each would let the two disagree.
    /// </remarks>
    ValueTask<KillIdentification> IdentifyAsync(
        DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken);

    /// <summary>Whether every monster Actor in the current Cell is dead.</summary>
    ValueTask<bool> IsCurrentRoomClearedAsync(
        DungeonRunState run, CancellationToken cancellationToken);
}
