using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The server's record of who is present, with somebody in it.
/// </summary>
/// <remarks>
/// A character with no record here is refused an item outright, because entry admits a character
/// to this record in the same step that publishes the runtime state a use used to be judged
/// against. So a case that is not about that window has to put its character in, and this is the
/// smallest way to: standing in a town, which is the only kind of place this server serves.
/// </remarks>
internal static class PresentCharacters
{
    /// <summary>A record admitting each of them to a town, and nobody else.</summary>
    public static ISessionPresenceDomainService StandingInTown(params CharacterId[] characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        var presence = new SessionPresenceDomainService();
        foreach (var character in characters)
        {
            presence.Admit(new SessionPresence(
                character, ListenerPort: 10011, TownId: 0, AreaId: 0, StandingInTown: true));
        }

        return presence;
    }
}

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
/// A character's own row: either a character of some job standing at some level, or none at all.
/// </summary>
/// <remarks>
/// Two answers from one double because the pair is the point. What a character may use is now
/// judged against its own row rather than against the session's copy of it -- both the level it
/// stands at and the job it took up -- so a case about that judgement has to be able to say both
/// "the row says forty" and "there is no row", and the second is not the first with a zero in it.
/// <para>
/// Everything the table can be asked besides what the row says about this character refuses
/// outright, because no case here has needed it.
/// </para>
/// </remarks>
internal sealed class CharacterRow : ICharacterTable
{
    private readonly byte? level;

    private readonly byte job;

    private CharacterRow(byte? level, byte job = 0)
    {
        this.level = level;
        this.job = job;
    }

    /// <summary>A character this server does not have.</summary>
    public static CharacterRow None { get; } = new(null);

    /// <summary>A character whose row says it has reached that level, of that job.</summary>
    /// <remarks>
    /// Job zero is the swordman, and it is the default because most cases here are not about the
    /// job; the ones that are name it.
    /// </remarks>
    public static CharacterRow At(byte level, byte job = 0) => new(level, job);

    /// <summary>What the row holds, which no case here moves.</summary>
    public const uint Experience = 700;

    /// <summary>The slot and name the row carries, which no case here reads for its value.</summary>
    public const ushort SlotIndex = 2;

    public ValueTask<CharacterProgressRow?> FindProgressAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            this.level is { } standing ? new CharacterProgressRow(standing, Experience) : null);

    public ValueTask<CharacterStandingRow?> FindStandingAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            this.level is { } standing
                ? new CharacterStandingRow(this.job, GrowType: 0, standing)
                : null);

    public ValueTask<CharacterIdentityRow?> FindIdentityAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            this.level is null
                ? null
                : new CharacterIdentityRow(SlotIndex, [0x41], this.job, GrowType: 0));

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

    public ValueTask<int> SetGrowTypeAsync(CharacterId characterId, int expectedGrowType, int growType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
    public ValueTask<CharacterSkillPointLedger?> LedgerAsync(CharacterId characterId, CancellationToken cancellationToken) => throw new NotSupportedException();

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
