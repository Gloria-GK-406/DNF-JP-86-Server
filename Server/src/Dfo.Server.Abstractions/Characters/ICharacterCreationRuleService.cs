using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>What is wrong with a proposed character name, or nothing.</summary>
public enum CharacterNameShape
{
    WellFormed,
    TooShort,
    TooLong,
    Malformed,
}

/// <summary>What a new character of one job is born with.</summary>
/// <param name="Skills">
/// Already in their slots. Which band a grant lands in is a rule about the client's skill bar, not
/// about the archive's order, so it is decided here rather than by whoever writes the rows.
/// </param>
public sealed record NewCharacterStartingState(
    CharacterBaseStats Stats,
    IReadOnlyList<NewCharacterSkill> Skills,
    IReadOnlyList<NewCharacterEquipment> Equipment);

/// <summary>
/// What the game permits when a character is made, and what it is made with.
/// </summary>
/// <remarks>
/// Three archive declarations and four bounds answer to one subject. The stats, the initial skills
/// and the initial equipment say what a job starts with; the name bounds, the job ceiling and the
/// account's slot limit say what may be asked for. None of them is a fact about any character that
/// exists, which is why they are here rather than beside the rows a creation writes.
/// </remarks>
public interface ICharacterCreationRuleService
{
    /// <summary>How many characters one account may hold.</summary>
    ushort AccountSlotLimit { get; }

    /// <summary>Whether the game has a job under this identifier.</summary>
    bool IsKnownJob(byte job);

    /// <summary>
    /// Whether a name is well formed for the name-check protocol, which accepts the longer bound.
    /// </summary>
    CharacterNameShape InspectNameForCheck(byte[]? rawName);

    /// <summary>
    /// Whether a name is well formed for creation, which accepts the shorter bound.
    /// </summary>
    /// <remarks>
    /// A name can legitimately pass the check and then be refused here. The two protocols carry
    /// different bounds and the legacy server behaved the same way, so the client copes with it.
    /// </remarks>
    CharacterNameShape InspectNameForCreation(byte[]? rawName);

    /// <summary>What a new character of this job is born with, in its slots.</summary>
    ValueTask<NewCharacterStartingState> StartingStateAsync(
        byte job, CancellationToken cancellationToken);
}
