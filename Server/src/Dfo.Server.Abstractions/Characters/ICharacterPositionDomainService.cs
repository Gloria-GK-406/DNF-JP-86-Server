using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// Where each character being played is standing, and the only place that answer moves.
/// </summary>
/// <remarks>
/// The position used to be a field on the session's snapshot of the character, which meant every
/// step had to read the whole snapshot out, copy it with one record replaced, and write the whole
/// thing back. Anything else holding that snapshot then had a position in it that no longer
/// matched, and the only way to ask where somebody was standing was to find their session first.
/// It is asked by character here, and the two commands that move one report the movement they
/// carry rather than a new snapshot.
/// <para>
/// What it holds is the live position, not the stored one. The stored one is a row, written when
/// the character enters and again when their session is torn down; those two writes are the
/// persistence seam's business and this service does not perform them. Nothing is prepared for a
/// character who is not being played and asking about one answers nothing.
/// </para>
/// </remarks>
public interface ICharacterPositionDomainService
{
    /// <summary>
    /// Records where an entering character stands, replacing any earlier record of them.
    /// </summary>
    /// <remarks>
    /// Takes the session rather than the character alone, because the record has to know which
    /// generation put it there: a session that has already been displaced must not take its
    /// replacement's position down with it when it is torn down.
    /// </remarks>
    void Place(SelectedSessionIdentity session, CharacterPositionState position);

    /// <summary>Where this character is standing, or null when nobody is playing them.</summary>
    CharacterPositionState? Find(CharacterId characterId);

    /// <summary>
    /// Records the character crossing into another area, and answers where they now stand.
    /// </summary>
    /// <remarks>
    /// This is also how a character crosses between towns, so the destination carries both bytes.
    /// The facing is kept: the command that reports a crossing does not carry one.
    /// <para>
    /// Throws when the record standing there is not this session's, which is what replacing the
    /// session's whole runtime state used to do and is the answer a displaced session has to get:
    /// its report would otherwise move the character who displaced it.
    /// </para>
    /// </remarks>
    CharacterPositionState CrossInto(
        SelectedSessionIdentity session, byte townId, byte areaId, short x, short y);

    /// <summary>
    /// Records the character taking a step inside the area they are already in, and answers where
    /// they now stand.
    /// </summary>
    /// <remarks>
    /// The town and area are kept, and that is the difference between this and
    /// <see cref="CrossInto"/> rather than an omission. A step report that carried an area would
    /// let a frame sent before a crossing arrive after it and contradict it.
    /// <para>
    /// Refuses a session that is no longer the one standing there, for the reason
    /// <see cref="CrossInto"/> gives.
    /// </para>
    /// </remarks>
    CharacterPositionState StepTo(
        SelectedSessionIdentity session, short x, short y, byte direction);

    /// <summary>Forgets this session's position. False when the record is not this session's.</summary>
    bool ForgetExact(SelectedSessionIdentity session);
}
