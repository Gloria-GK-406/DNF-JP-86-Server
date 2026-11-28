using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal readonly record struct QuestExperienceBenefitContext(CharacterIdentityState Character, ushort QuestId);

internal sealed class QuestExperienceBenefits(ILuckyCharacterEventQuery luckyCharacters, IQuestContentRuleService quests)
    : IGameplayBenefitSource<QuestExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        QuestExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var period = await luckyCharacters.ReadAtAsync(at, ct).ConfigureAwait(false);
        var eligible = period.Includes(context.Character.Job)
            && await quests.FindAsync(context.QuestId, ct).ConfigureAwait(false) is
                { IsEventOnly: not true, Grade: QuestGrade.Epic or QuestGrade.Normal or QuestGrade.Side };
        return new("lucky-character", GameplayBenefitScope.For(context.Character.CharacterId), eligible
            ? [new("quest-experience", "lucky character", 700,
                new ExperienceBenefit(ExperienceEvent.QuestCompletion, new("lucky character", 0.10, ExperienceBonusKind.LuckyCharacter)))]
            : []);
    }
}
