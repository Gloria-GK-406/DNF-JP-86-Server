using Dfo.Server.Abstractions.Content;
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
    IQuestDefinitionSource quests,
    IDailyActivityDefinitionSource dailyActivities,
    ICollectionBoxDefinitionSource collectionBoxes,
    ICreatureDefinitionSource creatures) : IWorldContentEntryRuleService
{
    public async ValueTask<WorldContentEntryState> LoadAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new WorldContentEntryState(
            await ResolveAvailableQuestsAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolveDailyChallengeAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolveCollectionBoxesAsync(subject, cancellationToken).ConfigureAwait(false),
            await ResolvePetWelcomeAsync(subject, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask<AvailableQuestEntryState> ResolveAvailableQuestsAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        var catalog = await quests.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var trainingAvailability = await quests
            .GetTrainingAvailabilityAsync(cancellationToken)
            .ConfigureAwait(false);
        var equippedCreature = subject.EquippedCreatureItemId > 0
            ? await creatures.FindByEquipmentAsync(
                subject.EquippedCreatureItemId,
                cancellationToken).ConfigureAwait(false)
            : null;
        var requirement = CharacterJobFamilies.Resolve(subject.Job);
        var offerable = new List<OfferableQuestState>();
        var seenQuestIds = new HashSet<int>();

        foreach (var quest in catalog)
        {
            if (quest.QuestId is <= 0 or > 29_999 || !seenQuestIds.Add(quest.QuestId))
                continue;
            if (quest.IsExposedByNpc == false || quest.IsEventOnly == true)
                continue;
            if (quest.ExpertJobRequirement is not null)
                continue;
            if (!MatchesTargetCharacter(quest.TargetCharacterRestriction, requirement))
                continue;
            if (!MatchesTrainingAvailability(quest, subject.Level, trainingAvailability))
                continue;
            if (!await MatchesCreatureRestrictionAsync(
                    quest,
                    subject,
                    equippedCreature,
                    cancellationToken).ConfigureAwait(false))
                continue;
            if (!MatchesQuestProgress(quest, subject.ClearedQuestFlags))
                continue;
            // Being carried is not being offerable. The reference server's own list builder is not
            // given the carried set at all and so cannot make this distinction; its list names a
            // quest the character is already holding, the client believes it and raises its accept
            // prompt again, and the second acceptance is refused. Deviating from its bytes on
            // purpose -- recorded in Docs/ProtocolScaffoldDissonances.md.
            if (subject.CarriedQuestIds.Contains(quest.QuestId))
                continue;
            if (!IsOfferableGrade(quest.Grade))
                continue;
            // A repeatable quest is offered again once cleared; that is what repeatable
            // means. Excluding every cleared quest would retire the dailies permanently
            // after their first completion.
            if (!IsRepeatable(quest.Grade) && subject.ClearedQuestFlags.ContainsKey(quest.QuestId))
                continue;
            if (quest.RequiredLevel is { } requiredLevel && !requiredLevel.Includes(subject.Level))
                continue;
            if (quest.JobRestriction is { } jobRestriction
                && (!jobRestriction.IsValid
                    || !jobRestriction.IsExactAll
                    && !jobRestriction.JobFamilies.Contains(requirement)))
                continue;
            if (!MatchesGrowType(quest, subject.GrowType))
                continue;

            offerable.Add(new OfferableQuestState(
                quest.QuestId,
                quest.Name,
                quest.GrowTypeRequirement ?? QuestDefinition.AnyGrowType));
        }

        return new AvailableQuestEntryState(offerable);
    }

    private static bool MatchesTargetCharacter(
        QuestTargetCharacterRestriction? restriction,
        QuestJobRequirement characterJob) =>
        restriction is null
        || restriction.IsValid
            && characterJob is not QuestJobRequirement.Unrecognized
            && restriction.JobFamilies.Contains(characterJob);

    private static bool MatchesTrainingAvailability(
        QuestDefinition quest,
        int characterLevel,
        IReadOnlyList<TrainingQuestAvailability> availability)
    {
        if (quest.Grade is not QuestGrade.Training)
            return true;
        if (quest.NpcIndex is not > 0)
            return false;

        return availability.Any(level =>
            level.CharacterLevel == characterLevel
            && level.NpcIndexes.Contains(quest.NpcIndex.Value));
    }

    private async ValueTask<bool> MatchesCreatureRestrictionAsync(
        QuestDefinition quest,
        WorldContentSubject subject,
        CreatureDefinition? equippedCreature,
        CancellationToken cancellationToken)
    {
        if (quest.CreatureKindRequirement is null)
            return true;
        if (equippedCreature is null
            || equippedCreature.CreatureId != quest.CreatureKindRequirement
            || subject.EquippedCreatureLevel <= 0)
        {
            return false;
        }

        foreach (var evolution in equippedCreature.Evolutions)
        {
            if (evolution.RequiredQuestId is not > 0
                || evolution.RequiredLevel is not > 0
                || subject.EquippedCreatureLevel < evolution.RequiredLevel)
            {
                continue;
            }

            // The target must itself resolve to creature equipment. A positive target ID
            // alone is not viable: the legacy evolution catalogue rejects that declaration
            // before it adds the current creature kind to the allowed set.
            return await creatures.CanResolveEvolutionEquipmentAsync(
                subject.EquippedCreatureItemId,
                evolution.EvolvesIntoCreatureId,
                cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool MatchesGrowType(QuestDefinition quest, byte characterGrowType)
    {
        if (quest.GrowTypeRequirement is not { } requiredGrowType)
            return true;

        return quest.JobChangeQuestCode switch
        {
            2 or 3 => requiredGrowType == (characterGrowType & 0xF),
            1 or 10 or 20 => true,
            _ => requiredGrowType == characterGrowType,
        };
    }

    private static bool MatchesQuestProgress(
        QuestDefinition quest,
        IReadOnlyDictionary<int, int> clearedFlags)
    {
        if (quest.CompletedQuestPrerequisiteGroups.Count > 0
            && !quest.CompletedQuestPrerequisiteGroups.Any(
                group => group.All(clearedFlags.ContainsKey)))
        {
            return false;
        }

        foreach (var requiredAnswer in quest.RequiredQuestAnswers)
        {
            if (!clearedFlags.TryGetValue(requiredAnswer.QuestId, out var actualFlag)
                || actualFlag != requiredAnswer.AnswerIndex + 1)
            {
                return false;
            }
        }

        if (quest.CollisionQuestIds.Any(clearedFlags.ContainsKey))
            return false;

        // This standalone entry composition has no complete account-cleared quest state.
        // A declared account collision therefore cannot be decided and must fail closed.
        return quest.AccountCollisionQuestIds.Count == 0;
    }

    /// <summary>
    /// Whether the content's classification of a quest makes it offerable at all.
    /// </summary>
    /// <remarks>
    /// Reverse-engineered from the legacy quest index's selectable-grade list. The grades
    /// left out — cracks, challenges, titles, schedules, urgent, wedding and village-attack
    /// revenge — are reached some other way, and offering them in the entry list would put
    /// quests in front of characters the legacy server never shows them to.
    /// <para>
    /// A null grade is the content's explicit optional absence and is offerable. An
    /// <see cref="QuestGrade.Unrecognized"/> value, by contrast, is a declared value the
    /// neutral contract cannot decide and therefore fails closed.
    /// </para>
    /// </remarks>
    private static bool IsOfferableGrade(QuestGrade? grade) => grade is
        null
        or QuestGrade.Normal
        or QuestGrade.Training
        or QuestGrade.Side
        or QuestGrade.Sub
        or QuestGrade.Epic
        or QuestGrade.Achievement
        or QuestGrade.CommonUnique
        or QuestGrade.System
        or QuestGrade.Daily
        or QuestGrade.DailyRandom
        or QuestGrade.NormallyRepeat
        or QuestGrade.SpecialDaily;

    /// <remarks>
    /// The legacy exemption names three grades. A random daily is <em>not</em> among them,
    /// even though it is offerable, so it is excluded here too rather than grouped with the
    /// other dailies on the strength of its name.
    /// </remarks>
    private static bool IsRepeatable(QuestGrade? grade) => grade is
        QuestGrade.Daily
        or QuestGrade.NormallyRepeat
        or QuestGrade.SpecialDaily;

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
