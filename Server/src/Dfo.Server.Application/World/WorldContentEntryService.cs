using Dfo.Server.Abstractions.Content;
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
internal sealed class WorldContentEntryService(
    IQuestDefinitionSource quests,
    IDailyActivityDefinitionSource dailyActivities,
    ICollectionBoxDefinitionSource collectionBoxes,
    ICreatureDefinitionSource creatures) : IWorldContentEntryService
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
        var requirement = QuestJobRequirementFor(subject.Job);
        var offerable = new List<OfferableQuestState>();

        foreach (var quest in catalog)
        {
            if (!IsOfferableGrade(quest.Grade))
                continue;
            // A repeatable quest is offered again once cleared; that is what repeatable
            // means. Excluding every cleared quest would retire the dailies permanently
            // after their first completion.
            if (!IsRepeatable(quest.Grade) && subject.ClearedQuestIds.Contains(quest.QuestId))
                continue;
            if (!quest.RequiredLevel.Includes(subject.Level))
                continue;
            if (quest.JobRequirement is not QuestJobRequirement.Any && quest.JobRequirement != requirement)
                continue;
            if (!quest.AppliesToAnyGrowType && quest.GrowTypeRequirement != subject.GrowType)
                continue;

            offerable.Add(new OfferableQuestState(quest.QuestId, quest.Name, quest.GrowTypeRequirement));
        }

        return new AvailableQuestEntryState(offerable);
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
    /// <see cref="QuestGrade.Unrecognized"/> is offerable because the legacy list admits a
    /// quest that states no grade, and in this archive that is the only thing it can mean:
    /// the content source's own vocabulary test fails if the archive ever yields a grade
    /// token the enumeration does not model. Were that test to start failing, this
    /// treatment would have to be revisited before the enumeration is widened.
    /// </para>
    /// </remarks>
    private static bool IsOfferableGrade(QuestGrade grade) => grade is
        QuestGrade.Unrecognized
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
    private static bool IsRepeatable(QuestGrade grade) => grade is
        QuestGrade.Daily
        or QuestGrade.NormallyRepeat
        or QuestGrade.SpecialDaily;

    /// <summary>
    /// Maps a character's job number onto the job family the content names.
    /// </summary>
    /// <remarks>
    /// Reverse-engineered from the legacy quest index, which resolves a job number to one of
    /// seven base families and separately decides whether the awakened spelling applies.
    /// Two pairs collapse: job 8 and job 10 are both the awakened mage, and job 9 and job 11
    /// are both the awakened swordman.
    /// <para>
    /// <see cref="QuestJobRequirement.DemonicSwordman"/> is deliberately unreachable here.
    /// The content declares it, but no job number maps to it in the legacy table, so no
    /// character is currently offered a quest restricted to it. Inventing a mapping would be
    /// a behaviour change dressed as a port, so the gap is left visible instead.
    /// </para>
    /// </remarks>
    private static QuestJobRequirement QuestJobRequirementFor(byte job) => job switch
    {
        0 => QuestJobRequirement.Swordman,
        1 => QuestJobRequirement.Fighter,
        2 => QuestJobRequirement.Gunner,
        3 => QuestJobRequirement.Mage,
        4 => QuestJobRequirement.Priest,
        5 => QuestJobRequirement.AwakenedGunner,
        6 => QuestJobRequirement.Thief,
        7 => QuestJobRequirement.AwakenedFighter,
        8 or 10 => QuestJobRequirement.AwakenedMage,
        9 or 11 => QuestJobRequirement.AwakenedSwordman,
        12 => QuestJobRequirement.Knight,
        _ => QuestJobRequirement.Unrecognized,
    };

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
                cells));
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
