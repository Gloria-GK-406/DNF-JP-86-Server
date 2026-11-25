using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpTotalMatchCandidateQueryDomainService(
    IPvpTotalMatchTeamDomainStore teams,
    ICharacterRosterDomainService roster,
    ICharacterEntryStateDomainService entries,
    ICharacterInventoryEntryDomainService inventories,
    ICharacterAppearanceRuleService appearance,
    ICharacterPvpSkillStateTable skillState,
    CharacterSkillFactsReader skillFacts,
    ICharacterEntryPreparationRuleService preparationRules,
    ICharacterSkillStateDomainService builds,
    IPvpRecordDomainStore records,
    PvpRatingSeason season) : IPvpTotalMatchCandidateQueryDomainService
{
    public async ValueTask<PvpTotalMatchLineup?> ReadAsync(
        SelectedSessionIdentity session, DateTimeOffset preparedAt, CancellationToken cancellationToken)
    {
        var account = session.AccountId;
        if (await teams.ReadAsync(account, cancellationToken).ConfigureAwait(false) is not { } team
            || team.Members.Length != PvpTotalMatchTeam.MemberCount
            || team.Members[0] != session.CharacterId)
            return null;

        var slots = (await roster.GetAccountRosterAsync(account, cancellationToken).ConfigureAwait(false))
            .Characters.ToDictionary(character => character.CharacterId, character => character.SlotIndex);
        if (team.Members.Any(id => !slots.ContainsKey(id))) return null;

        var candidates = ImmutableArray.CreateBuilder<PvpTotalMatchCandidate>(team.Members.Length);
        foreach (var id in team.Members)
        {
            var character = await roster.LoadForSelectionAsync(account, slots[id], cancellationToken).ConfigureAwait(false);
            if (character.Identity.CharacterId != id) return null;
            var entry = await entries.LoadAsync(account, id, cancellationToken).ConfigureAwait(false);
            var inventory = await inventories.ReadCurrentAsync(account, id, cancellationToken).ConfigureAwait(false);
            inventory = await SupportCreatureMarkerProjection.CompleteAsync(
                inventory, preparationRules, preparedAt, cancellationToken).ConfigureAwait(false);
            var equipment = inventory.Containers.SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)
                ?.Items ?? [];
            var publicState = CharacterStateProjection.CompleteAppearanceProjection(appearance, character, entry, inventory).Public;

            // A reserve can be used before it has entered a duel channel. Derive its initial page without persisting it.
            var pages = await skillState.FindAsync(id, cancellationToken).ConfigureAwait(false) is { SkillsInitialized: not 0 }
                ? await skillFacts.ReadDuelPagesAsync(id, cancellationToken).ConfigureAwait(false)
                : await PvpSkillInitializer.BuildInitialPagesAsync(character, preparationRules, cancellationToken).ConfigureAwait(false);
            var build = (await builds.BuildsAsync(id, cancellationToken, SkillGroup.Duel).ConfigureAwait(false)).Active;
            candidates.Add(new(character, publicState, equipment, pages.Single(page => page.PageIndex == build.Value)));
        }
        return new(team.Name, candidates.MoveToImmutable())
        {
            GroupPoints = (await records.ReadTotalMatchRatingAsync(session.CharacterId, season.Id, cancellationToken)
                .ConfigureAwait(false)).OfficialPoints,
        };
    }
}
