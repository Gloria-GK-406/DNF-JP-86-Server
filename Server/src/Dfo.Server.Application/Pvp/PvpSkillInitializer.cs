using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSkillInitializer(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterPvpSkillStateTable skillState,
    CharacterSkillFactsReader skillFacts,
    ICharacterEntryPreparationRuleService rules) : ICharacterSkillGroupInitializer
{
    public SkillGroup Group => SkillGroup.Duel;

    public ValueTask<IReadOnlyList<CharacterSkillPageState>> PrepareAsync(
        AccountId accountId, ActiveCharacterState character, CancellationToken cancellationToken) =>
        LoadOrInitializeAsync(accountId, character.Identity.CharacterId,
            ct => BuildInitialPagesAsync(character, rules, ct), cancellationToken);

    internal async ValueTask<IReadOnlyList<CharacterSkillPageState>> LoadOrInitializeAsync(
        AccountId accountId, CharacterId characterId,
        Func<CancellationToken, ValueTask<IReadOnlyList<CharacterSkillPageState>>> createInitialPages,
        CancellationToken cancellationToken)
    {
        if (await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false) != accountId)
            throw new InvalidDataException("PvP skill profile owner does not match the account.");

        var state = await skillState.FindAsync(characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Character {characterId.Value} has no duel skill-state row to seed skills against.");
        if (state.SkillsInitialized == 0)
        {
            var pages = await createInitialPages(cancellationToken).ConfigureAwait(false);
            foreach (var page in pages)
            foreach (var skill in page.Skills)
            {
                await skills.InsertAsync(characterId, page.PageIndex, skill.Slot, (int)skill.SkillId,
                    skill.Level, cancellationToken, Group, [.. skill.ExtraValues]).ConfigureAwait(false);
            }

            await skillState.MarkSkillsInitializedAsync(characterId, cancellationToken).ConfigureAwait(false);
        }
        return await skillFacts.ReadDuelPagesAsync(characterId, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<IReadOnlyList<CharacterSkillPageState>> BuildInitialPagesAsync(
        ActiveCharacterState character,
        ICharacterEntryPreparationRuleService rules, CancellationToken cancellationToken)
    {
        var definitions = await rules.SkillPointsAsync(character.Identity.Job, character.Identity.GrowType,
            character.Progression.Level, [], cancellationToken).ConfigureAwait(false);
        var grants = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .Select(group => group.OrderByDescending(value => value.Level).First()).ToArray();
        var occupied = new HashSet<int>();
        var skills = new List<CharacterSkillState>();
        foreach (var grant in grants.OrderBy(value => value.IsInitial ? 0 : 1))
        {
            definitions.Skills.TryGetValue(grant.SkillId, out var definition);
            var active = definition?.IsActive == true;
            var grantLevel = grant.Level;
            if (definition is not null)
                grantLevel = checked((byte)Math.Clamp(SkillPointLedgerRules.FreeLevel(
                    definition, grantLevel, character.Progression.Level), 0, byte.MaxValue));
            int slot;
            if (grant.IsInitial)
                slot = FirstFree(active ? 0 : 102, active ? 102 : 204);
            else
            {
                var group = ReformGroup(definition?.RawGroup ?? 0, active, definition?.NumberOfGrowTypes ?? 0);
                slot = active && character.Identity.Job != 9
                    ? FirstFree(0, 6) is var primary && primary >= 0 ? primary : FirstFree(198, 204)
                    : FirstFree(new[] { 6, 54, 102, 150 }[Math.Clamp(group, 0, 3)],
                        new[] { 54, 102, 150, 198 }[Math.Clamp(group, 0, 3)]);
            }
            if (slot < 0) slot = FirstFree(0, 204);
            if (slot < 0) throw new InvalidDataException("PvP initial skill profile has no free slot.");
            occupied.Add(slot);
            skills.Add(new CharacterSkillState(checked((short)slot), checked((ushort)grant.SkillId), grantLevel, []));
        }
        return [new CharacterSkillPageState(0, skills), new CharacterSkillPageState(1, skills)];

        int FirstFree(int start, int end)
        { for (var index = start; index < end; index++) if (!occupied.Contains(index)) return index; return -1; }
        static int ReformGroup(int raw, bool active, int grows) => active ? 3
            : raw is >= 0 and <= 3 ? grows <= 2 ? 1 : 0 : raw == 4 ? 2 : raw;
    }

}
