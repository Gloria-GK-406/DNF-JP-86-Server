using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// A character's gold, for the cases that are not about gold.
/// </summary>
/// <remarks>
/// <see cref="PlayingCharacter"/> answers questions about item use as well as about money, and the
/// cases about item use have to construct one. This stands in for the money half so that they can,
/// and it answers as a character this server does not have: nothing is carried, nothing is given,
/// nothing can be taken. A case that started to depend on gold would fail against it rather than
/// quietly pass against a made-up figure.
/// </remarks>
internal sealed class NoCarriedGold : ICharacterCarriedGoldTable
{
    public ValueTask<CarriedGoldRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CarriedGoldRow?>(null);

    public ValueTask<int> AddAsync(
        CharacterId characterId, int amount, int ceiling, CancellationToken cancellationToken) =>
        ValueTask.FromResult(0);

    public ValueTask<int?> TryTakeAsync(
        CharacterId characterId, int amount, CancellationToken cancellationToken) =>
        ValueTask.FromResult<int?>(null);
}

/// <summary>
/// A character's own row: either a character standing at some level, or none at all.
/// </summary>
/// <remarks>
/// Two answers from one double because the pair is the point. What a character may use is now
/// judged against the level on its own row rather than against the session's copy of it, so a case
/// about that judgement has to be able to say both "the row says forty" and "there is no row" --
/// and the second is not the first with a zero in it.
/// <para>
/// Everything the table can be asked besides those two refuses outright, because no case here has
/// needed it.
/// </para>
/// </remarks>
internal sealed class CharacterRow : ICharacterTable
{
    private readonly byte? level;

    private CharacterRow(byte? level) => this.level = level;

    /// <summary>A character this server does not have.</summary>
    public static CharacterRow None { get; } = new(null);

    /// <summary>A character whose row says it has reached that level.</summary>
    public static CharacterRow At(byte level) => new(level);

    /// <summary>What the row holds, which no case here moves.</summary>
    public const uint Experience = 700;

    public ValueTask<CharacterProgressRow?> FindProgressAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            this.level is { } standing ? new CharacterProgressRow(standing, Experience) : null);

    public ValueTask<CharacterStandingRow?> FindStandingAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            this.level is { } standing
                ? new CharacterStandingRow(Job: 0, GrowType: 0, standing)
                : null);

    public ValueTask<IReadOnlyList<CharacterRosterRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<IReadOnlyList<CharacterSummaryRow>> ReadSummariesByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

    public ValueTask<CharacterSummaryRow?> FindBySlotAsync(
        AccountId accountId, int slotIndex, CancellationToken cancellationToken) => throw Unneeded();

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

    public ValueTask<int> SetProgressAsync(
        CharacterId characterId, int level, long experience, CancellationToken cancellationToken) =>
        throw Unneeded();

    public ValueTask<bool> MarkDeletedAsync(
        CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

    private static NotSupportedException Unneeded() =>
        new("This double answers where a character stands and nothing else.");
}

/// <summary>
/// A skill-point ledger for the cases that are not about skill points.
/// </summary>
/// <remarks>
/// Answers as a character this server does not have, which is what a ledger over
/// <see cref="CharacterRow.None"/> would answer anyway.
/// </remarks>
internal sealed class NoSkillPointLedger : ICharacterSkillPointDomainService
{
    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(null);
}

/// <summary>A carry cap for the cases that are not about the carry cap.</summary>
/// <remarks>
/// It answers with the stored upgrade and reads no archive, which is enough for a construction
/// that never asks. A case that started to depend on the archive's floor would see this figure
/// rather than the real one and would have to say so.
/// </remarks>
internal sealed class StoredCarryLimitOnly : ICarriedGoldRuleService
{
    public int CarryLimitFor(int characterLevel, int storedCarryLimit) =>
        Math.Max(0, storedCarryLimit);
}
