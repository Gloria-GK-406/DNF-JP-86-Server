using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// A character's gold that remembers what it was given and what it paid.
/// </summary>
/// <remarks>
/// A stand-in rather than the real seam over SQLite, because what is under test in these slices is
/// what they ask a character for and what they do with the answer. That the store itself refuses a
/// charge it cannot meet, holds the figure at the carry cap, and does both in one statement so two
/// changes cannot race, is asserted against real SQLite in the persistence tests. The refusal is
/// modelled the same way here so that a slice which stopped reading it would fail here too.
/// </remarks>
internal class CarriedGold(int total) : ICharacter
{
    public CharacterId Id => new(60);

    public virtual ValueTask<Dfo.Server.Abstractions.World.ExperienceAward?> AddExpAsync(Dfo.Server.Abstractions.World.ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<Dfo.Server.Abstractions.World.ExperienceAward> AddExpAsync(Dfo.Server.Abstractions.World.ExperienceBreakdown reward, CharacterProgressionState? expected, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<CharacterProgressionState> SetExpAsync(long experience, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<CharacterProgressionState> SetLevelAsync(byte level, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<bool> TrySetExpAsync(CharacterProgressionState expected, long experience, CancellationToken ct) => throw new NotSupportedException();

    public ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken cancellationToken) => throw new NotSupportedException();


    public int Total { get; private set; } = total;

    /// <summary>How many charges were attempted, met or not.</summary>
    public int Charges { get; private set; }

    public ValueTask<ItemUseCandidate?> ItemUseFactsAsync(ItemCooldownKey key, CancellationToken token) => throw new NotSupportedException();

    public ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("These cases are about gold, not about using an item.");

    public ValueTask<CharacterProgressionState?> ProgressAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<CharacterProgressionState?>(new(60, 0));

    public ValueTask<CharacterIdentityState?> IdentityAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "These cases are about gold, not about what the character is.");

    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("These cases are about gold, not about skill points.");

    public ValueTask<int> GoldAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(this.Total);

    public ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken)
    {
        this.Total = Math.Max(0, this.Total + amount);
        return ValueTask.FromResult(this.Total);
    }

    public ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken cancellationToken)
    {
        this.Charges++;
        if (amount > this.Total)
            return ValueTask.FromResult<int?>(null);

        this.Total -= amount;
        return ValueTask.FromResult<int?>(this.Total);
    }
}

/// <summary>The one character these cases play, reached the way business reaches one.</summary>
internal class OnePlayingCharacter(ICharacter character) : ICharacterDomainService
{
    public virtual ICharacter Get(CharacterId characterId) => character;

    public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(
        AccountId accountId, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
        AccountId accountId, ushort slotIndex, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
        AccountId accountId, ushort rosterIndex, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<CharacterNameStatus> CheckNameAsync(
        byte[] rawName, CancellationToken cancellationToken) => throw Unused();

    public ValueTask<CharacterCreationResult> CreateCharacterAsync(
        AccountId accountId, byte job, byte[] rawName, CancellationToken cancellationToken) =>
        throw Unused();

    public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
        AccountId accountId,
        ushort slotIndex,
        byte[] rawName,
        CancellationToken cancellationToken) => throw Unused();

    private static NotSupportedException Unused() =>
        new("These cases reach a character only for its gold.");
}
