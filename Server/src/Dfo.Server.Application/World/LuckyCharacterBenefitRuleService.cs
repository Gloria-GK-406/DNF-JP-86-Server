using System.Collections.Immutable;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class LuckyCharacterBenefitRuleService(IFatigueBurningDungeonContentSource restrictions)
{
    internal const string RunSource = "dungeon-run";
    internal const string BurningKey = "fatigue-burning";
    private const int FatigueMultiplier = 2;
    private const double KillAndQuestRate = 0.10;

    public GameplayBenefit Burning { get; } = new(BurningKey, "fatigue burning", 900,
        [new FatigueCostMultiplier(FatigueMultiplier, RequireFullCharge: true),
            new ExperienceBenefit([ExperienceEvent.MonsterKill, ExperienceEvent.DungeonClear],
                new("fatigue burning", 1, ExperienceBonusKind.FatigueBurn, ExperienceBonusBasis.TotalAfterBaseBonuses))]);

    public bool CanEnableBurning(ushort remaining) => remaining >= FatigueMultiplier;

    public bool AllowsDungeonBurning(int dungeonId) => !restrictions.LoadProhibitedDungeons().Contains(dungeonId);

    public ImmutableArray<GameplayBenefit> RunExperience(ExperienceOccasion occasion, DungeonRunState? run)
    {
        if (occasion.RunId is not { } runId || run is not
            { Layout.Mode.Kind: DungeonRunKind.Ordinary, Participant: { } participant } || run.RunId != runId)
            return [];

        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        if (participant.IsLuckyCharacter)
            found.Add(DungeonExperienceBenefits.Entry("lucky-character", "lucky character", 700, occasion.Event,
                occasion.Event == ExperienceEvent.DungeonClear ? 0.15 : KillAndQuestRate, ExperienceBonusKind.LuckyCharacter));
        if (occasion.Event == ExperienceEvent.DungeonClear
            && run.Participants.Any(member => member.CharacterId != occasion.Character && member.IsLuckyCharacter
                && run.ClearParticipants.Any(eligible => eligible.CharacterId == member.CharacterId)))
            found.Add(DungeonExperienceBenefits.Entry("lucky-teammate", "lucky character party", 800, occasion.Event,
                0.10, ExperienceBonusKind.LuckyCharacterParty));
        if (participant.BurnsFatigue && (occasion.Event == ExperienceEvent.DungeonClear
            || run.Layout.EntryOnlyFatigue is not null || run.CurrentCellState.FatigueBurnedBy.Contains(occasion.Character)))
            found.Add(Burning);
        return found.ToImmutable();
    }

    public ImmutableArray<GameplayBenefit> QuestExperience(bool lucky, QuestDefinition? quest) =>
        lucky && quest is { IsEventOnly: not true, Grade: QuestGrade.Epic or QuestGrade.Normal or QuestGrade.Side }
            ? [DungeonExperienceBenefits.Entry("quest-experience", "lucky character", 700, ExperienceEvent.QuestCompletion,
                KillAndQuestRate, ExperienceBonusKind.LuckyCharacter)]
            : [];

    public ImmutableArray<GameplayBenefit> SharedDrops(IReadOnlyList<DungeonRunParticipant> participants) =>
        participants.Any(participant => participant.IsLuckyCharacter)
            ? [new("shared-equipment", "lucky character shared Unique equipment", 0,
                new SharedEquipmentDropBenefit(DropRarity.Unique, 3))]
            : [];
}
