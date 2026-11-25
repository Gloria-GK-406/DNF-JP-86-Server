using Dfo.Server.Abstractions.Characters;
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
    ICharacterEntryPreparationRuleService rules,
    ICharacterSkillStateDomainService builds) : ICharacterSkillGroupInitializer
{
    public SkillGroup Group => SkillGroup.Duel;

    public async ValueTask<(IReadOnlyList<CharacterSkillPageState> Pages, int SkillTreeIndex)> PrepareAsync(
        AccountId accountId, ActiveCharacterState character, CancellationToken cancellationToken)
    {
        var pages = await LoadOrInitializeAsync(accountId, character.Identity.CharacterId,
            ct => rules.InitialDuelSkillPagesAsync(character.Identity.Job, character.Identity.GrowType,
                character.Progression.Level, ct), cancellationToken);
        var skillBuilds = await builds.BuildsAsync(character.Identity.CharacterId, cancellationToken, Group)
            .ConfigureAwait(false);
        return (pages, skillBuilds.SkillTreeIndex);
    }

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
}
