using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal readonly record struct QuestExperienceBenefitContext(CharacterIdentityState Character, ushort QuestId);

internal sealed class QuestExperienceBenefits(ILuckyCharacterEventQuery luckyCharacters, IQuestContentRuleService quests,
    LuckyCharacterBenefitRuleService rules)
    : IGameplayBenefitSource<QuestExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        QuestExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var period = await luckyCharacters.ReadAtAsync(at, ct).ConfigureAwait(false);
        var lucky = period.Includes(context.Character.Job);
        var quest = lucky ? await quests.FindAsync(context.QuestId, ct).ConfigureAwait(false) : null;
        return new("lucky-character", GameplayBenefitScope.For(context.Character.CharacterId), rules.QuestExperience(lucky, quest));
    }
}
