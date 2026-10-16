using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

/// <summary>Eligibility belongs to Quest; World consumes the resulting offer projection.</summary>
internal sealed class QuestAvailabilityRuleService(IQuestContentRuleService quests,
    ICreatureDefinitionSource creatures) : IQuestAvailabilityRuleService
{
    public async ValueTask<AvailableQuestEntryState> ReadAvailableAsync(
        WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        var catalog = await quests.AllAsync(cancellationToken).ConfigureAwait(false);
        var trainingAvailability = await quests
            .TrainingAvailabilityAsync(cancellationToken)
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
            if (!await MeetsRequirementsAsync(quest, subject, requirement, trainingAvailability,
                    equippedCreature, cancellationToken).ConfigureAwait(false))
                continue;
            // Being carried is not being offerable. The reference server's own list builder is not
            // given the carried set at all and so cannot make this distinction; its list names a
            // quest the character is already holding, the client believes it and raises its accept
            // prompt again, and the second acceptance is refused. Deviating from its bytes on
            // purpose -- recorded in Docs/ProtocolScaffoldDissonances.md.
            if (subject.CarriedQuestIds.Contains(quest.QuestId))
                continue;
            if (!IsOfferableGrade(quest.Grade)
                && !(quest.Grade == QuestGrade.Wedding && subject.EligibleWeddingQuestIds.Contains(quest.QuestId)))
                continue;
            // A repeatable quest is offered again once cleared; that is what repeatable
            // means. Excluding every cleared quest would retire the dailies permanently
            // after their first completion.
            if (!IsRepeatable(quest.Grade) && subject.ClearedQuestFlags.ContainsKey(quest.QuestId))
                continue;

            offerable.Add(new OfferableQuestState(
                quest.QuestId,
                quest.Name,
                quest.GrowTypeRequirement ?? QuestDefinition.AnyGrowType));
        }

        return new AvailableQuestEntryState(offerable);
    }

    public async ValueTask<bool> MeetsRequirementsAsync(QuestDefinition quest,
        WorldContentSubject subject, CancellationToken cancellationToken)
    {
        var training = await quests.TrainingAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        var creature = subject.EquippedCreatureItemId > 0
            ? await creatures.FindByEquipmentAsync(subject.EquippedCreatureItemId, cancellationToken).ConfigureAwait(false) : null;
        return await MeetsRequirementsAsync(quest, subject, CharacterJobFamilies.Resolve(subject.Job), training,
            creature, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> MeetsRequirementsAsync(QuestDefinition quest, WorldContentSubject subject,
        QuestJobRequirement job, IReadOnlyList<TrainingQuestAvailability> training, CreatureDefinition? creature,
        CancellationToken cancellationToken) => quest.IsExposedByNpc != false && quest.IsEventOnly != true
        && quest.ExpertJobRequirement is null
        && MatchesTargetCharacter(quest.TargetCharacterRestriction, job)
        && MatchesTrainingAvailability(quest, subject.Level, training)
        && MatchesQuestProgress(quest, subject.ClearedQuestFlags)
        && (quest.RequiredLevel is not { } level || level.Includes(subject.Level))
        && (quest.JobRestriction is not { } restriction || restriction.IsValid
            && (restriction.IsExactAll || restriction.JobFamilies.Contains(job)))
        && MatchesGrowType(quest, subject.GrowType)
        && await MatchesCreatureRestrictionAsync(quest, subject, creature, cancellationToken).ConfigureAwait(false);

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
        or QuestGrade.Circle
        or QuestGrade.SpecialDaily;

}
