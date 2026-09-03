using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// One character's level and experience, held in memory.
/// </summary>
/// <remarks>
/// For tests about what a rule decides to write rather than about what a database does with it.
/// The progress moves as writes land, so a second award sees what the first left -- which is the
/// property being checked in most of them, and one a stub returning a constant would hide.
///
/// Everything the table can be asked besides progress refuses. What is left out is left out because
/// no test has needed it, and a double that answered plausibly for all of it would let a change
/// start depending on an answer nobody decided.
/// </remarks>
internal sealed class RecordingCharacterTable(int level, uint experience) : ICharacterTable
{
    /// <summary>Whether the character is there at all.</summary>
    public bool Exists { get; init; } = true;

    /// <summary>Whether a write lands. False stands for a row the store would not take.</summary>
    public bool AcceptWrites { get; init; } = true;

    /// <summary>The last level and experience written, or nothing when none was.</summary>
    public (int Level, uint Experience)? LastWrite { get; private set; }

    public ValueTask<CharacterProgressRow?> FindProgressAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(this.Exists ? new CharacterProgressRow(level, experience) : null);

    public ValueTask<int> SetProgressAsync(
        CharacterId characterId, int newLevel, long newExperience, CancellationToken cancellationToken)
    {
        if (!this.AcceptWrites)
            return ValueTask.FromResult(0);

        level = newLevel;
        experience = checked((uint)Math.Max(0, newExperience));
        this.LastWrite = (newLevel, experience);
        return ValueTask.FromResult(1);
    }

    public ValueTask<IReadOnlyList<CharacterRosterRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<IReadOnlyList<CharacterSummaryRow>> ReadSummariesByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<CharacterSummaryRow?> FindBySlotAsync(
        AccountId accountId, int slotIndex, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<CharacterStandingRow?> FindStandingAsync(
        CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<bool> NameIsTakenAsync(byte[] name, CancellationToken cancellationToken) =>
        throw Unneeded();

    public ValueTask<bool> BelongsToAsync(
        CharacterId characterId, AccountId accountId, CancellationToken cancellationToken) =>
        throw Unneeded();

    public ValueTask<AccountId?> FindOwnerAsync(
        CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<CharacterId> InsertAsync(
        AccountId accountId,
        string name,
        byte[] nameBytes,
        int job,
        int slotIndex,
        byte[] appearanceBlob,
        CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<bool> MarkDeletedAsync(
        CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

    private static NotSupportedException Unneeded() =>
        new("This double answers a character's progress and nothing else.");
}
