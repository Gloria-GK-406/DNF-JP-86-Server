using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Turns static content into the answers a character's entry carries.
/// </summary>
/// <remarks>
/// The rules live here rather than in the content sources on purpose: a source states what
/// the content declares, and deciding who qualifies for it is a rule that can change without
/// the content changing.
/// </remarks>
internal sealed class WorldContentEntryRuleService(
    IQuestContentRuleService quests,
    IDailyActivityDefinitionSource dailyActivities,
    ICollectionBoxDefinitionSource collectionBoxes,
    ICreatureDefinitionSource creatures, IQuestAvailabilityRuleService questAvailability) : IWorldContentEntryRuleService
{
    private readonly IQuestAvailabilityRuleService availability = questAvailability
        ?? new Dfo.Server.Application.Quests.QuestAvailabilityRuleService(quests, creatures);

    public async ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new WorldContentEntryState(
            await ReadOffersAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolveDailyChallengeAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolveCollectionBoxesAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolvePetWelcomeAsync(subject, cancellationToken).ConfigureAwait(false));
    }

    public ValueTask<AvailableQuestEntryState> ReadOffersAsync(WorldContentSubject subject, CancellationToken ct) =>
        availability.ReadAvailableAsync(subject, ct);

    public ValueTask<bool> MeetsQuestRequirementsAsync(QuestDefinition quest, WorldContentSubject subject, CancellationToken ct) =>
        availability.MeetsRequirementsAsync(quest, subject, ct);

    private async ValueTask<DailyChallengeEntryState> ResolveDailyChallengeAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        var groups = await dailyActivities.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var resolved = new List<DailyChallengeGroupEntryState>();

        foreach (var group in groups)
        {
            if (!group.Level.Includes(subject.Level))
                continue;

            var openCount = group.SlotCountForLevel(subject.Level);
            var open = new List<DailyChallengeSlotState>();
            foreach (var slot in group.Slots)
            {
                if (open.Count >= openCount)
                    break;

                open.Add(new DailyChallengeSlotState(slot.SlotIndex, slot.QuestIds));
            }

            var reward = RewardForLevel(group, subject.Level);
            resolved.Add(new DailyChallengeGroupEntryState(
                group.GroupId,
                openCount,
                open,
                subject.ClaimedDailyChallengeGroupIds.Contains(group.GroupId),
                reward.ItemTemplateId,
                reward.Count));
        }

        return new DailyChallengeEntryState(resolved);
    }

    private static DailyActivityReward RewardForLevel(DailyActivityGroupDefinition group, int level)
    {
        foreach (var reward in group.Rewards)
        {
            if (reward.Level.Includes(level))
                return reward;
        }

        return default;
    }

    private async ValueTask<IReadOnlyList<CollectionBoxEntryState>> ResolveCollectionBoxesAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        var definitions = await collectionBoxes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var resolved = new List<CollectionBoxEntryState>(definitions.Count);

        foreach (var definition in definitions)
        {
            var collected = CollectedFor(subject, definition.BoxIndex);
            var cells = new List<CollectionBoxCellState>(definition.Slots.Count);
            foreach (var slot in definition.Slots)
            {
                cells.Add(new CollectionBoxCellState(
                    slot.Column,
                    slot.Row,
                    slot.ItemTemplateId,
                    slot.Tier,
                    collected.Contains(slot.ItemTemplateId)));
            }

            resolved.Add(new CollectionBoxEntryState(
                definition.BoxIndex,
                definition.Columns,
                definition.Rows,
                cells,
                definition.MaximumExpiresAt));
        }

        return resolved;
    }

    private static IReadOnlyCollection<int> CollectedFor(WorldContentSubject subject, int boxIndex)
    {
        foreach (var progress in subject.CollectionBoxes)
        {
            if (progress.BoxIndex == boxIndex)
                return progress.CollectedItemTemplateIds;
        }

        return Array.Empty<int>();
    }

    private async ValueTask<PetWelcomeEntryState?> ResolvePetWelcomeAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        if (subject.EquippedCreatureItemId <= 0)
            return null;

        // Resolved through the equipment hop, not by identifier. An equipped creature is an
        // equipment identifier and the two numbering spaces do not overlap, so looking it up
        // as a creature would find nothing and read as "this character has no pet".
        var creature = await creatures
            .FindByEquipmentAsync(subject.EquippedCreatureItemId, cancellationToken)
            .ConfigureAwait(false);

        // No creature and a creature with no script are the same outcome for the consumer:
        // the packet is skipped. Neither is an error.
        return creature is null || !creature.HasAmbientLines
            ? null
            : new PetWelcomeEntryState(creature.CreatureId, creature.Name, creature.AmbientLines);
    }
}
