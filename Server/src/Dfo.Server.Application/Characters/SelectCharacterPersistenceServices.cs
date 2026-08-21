using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Brings a character's duelling skills into being the first time they are asked for.
/// </summary>
/// <remarks>
/// The marker row is the whole of the decision: its absence means these have never been set up,
/// and its presence means they have. Reading it, writing the skills, and writing the marker is a
/// sequence, and a sequence is a decision -- so it is made here.
/// </remarks>
internal sealed class PvpSkillProfileService(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterPvpTable pvp) : ICharacterPvpSkillProfileRepository
{
    public async ValueTask<CharacterSkillsEntryState> LoadOrInitializeAsync(
        AccountId accountId,
        CharacterId characterId,
        CharacterSkillsEntryState initialProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialProfile);
        var owner = await characters.FindOwnerAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        if (owner != accountId)
        {
            throw new InvalidDataException("PvP skill profile owner does not match the account.");
        }

        var pvpRow = await pvp.FindAsync(characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException(
                $"Character {characterId.Value} has no duel-arena row to seed skills against.");
        if (pvpRow.SkillsInitialized == 0)
        {
            foreach (var page in initialProfile.Pages)
            {
                foreach (var skill in page.Skills)
                {
                    await skills.InsertDuelSkillAsync(
                        characterId, page.PageIndex, skill.Slot, (int)skill.SkillId, skill.Level,
                        [.. skill.ExtraValues], cancellationToken).ConfigureAwait(false);
                }
            }

            await pvp.MarkSkillsInitializedAsync(characterId, cancellationToken)
                .ConfigureAwait(false);
        }

        var pages = new[] { new List<CharacterSkillState>(), new List<CharacterSkillState>() };
        foreach (var row in await skills.ReadDuelSkillsAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            if (row.PageIndex is < 0 or > 1
                || row.Slot is < short.MinValue or > short.MaxValue
                || row.SkillId is < 0 or > ushort.MaxValue
                || row.Level is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("PvP skill profile contains an out-of-range row.");
            }

            pages[row.PageIndex].Add(new CharacterSkillState(
                checked((short)row.Slot), checked((ushort)row.SkillId),
                checked((byte)row.Level), row.ExtraValues));
        }

        return new CharacterSkillsEntryState(
            [new CharacterSkillPageState(0, pages[0]), new CharacterSkillPageState(1, pages[1])],
            [], new CharacterSkillPointBasisState(0, 0), [], null,
            new CharacterSkillProtocolMirrorState(
                ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue));
    }
}

/// <summary>
/// Brings a character's skills back within what their expired contracts allow.
/// </summary>
/// <remarks>
/// Reads, asks, writes. What comes down and to what is the rule service's answer; which rows to
/// read and in what order to act is this one's. Neither of them opens a connection.
/// </remarks>
internal sealed class ExpertSkillContractReconciliationService(
    IAccountPremiumTable premiums,
    ICharacterTable characters,
    ICharacterSkillTable skills,
    IExpertSkillContractRuleService rules) : IExpertSkillContractReconciliationRepository
{
    public async ValueTask<bool> ReconcileAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlySet<int> overSkillPremiumTypes,
        IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline,
        CancellationToken cancellationToken)
    {
        if (overSkillPremiumTypes.Count == 0)
        {
            return false;
        }

        var held = await premiums.ReadAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!rules.HasRunOut(
                [.. held.Select(premium => (premium.PremiumType, premium.ExpiresAtUnixSeconds))],
                overSkillPremiumTypes,
                DateTimeOffset.UtcNow))
        {
            return false;
        }

        if (await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false)
            != accountId)
        {
            throw new InvalidDataException("Expert-contract skill owner is unavailable.");
        }

        var learned = await skills.ReadLevelsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var changes = rules.CapsFor(
            [.. learned.Select(row => (row.PageIndex, row.Slot, row.SkillId, row.Level))],
            definitions,
            freeBaseline);
        foreach (var change in changes)
        {
            var touched = change.Level is null
                ? await skills.RemoveAsync(
                    characterId, change.Page, change.Slot, cancellationToken).ConfigureAwait(false)
                : await skills.SetLevelAsync(
                    characterId, change.Page, change.Slot, change.Level.Value, cancellationToken)
                    .ConfigureAwait(false);
            if (touched != 1)
            {
                throw new InvalidDataException("Expert-contract skill row changed concurrently.");
            }
        }

        return changes.Count > 0;
    }
}
