using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal readonly record struct QuestExperienceBenefitContext(CharacterIdentityState Character, QuestDefinition Quest);

internal sealed class QuestExperienceBenefits(ILuckyCharacterEventQuery luckyCharacters, LuckyCharacterBenefitRuleService rules)
    : IGameplayBenefitSource<QuestExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        QuestExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var period = await luckyCharacters.ReadAtAsync(at, ct).ConfigureAwait(false);
        var lucky = period.Includes(context.Character.Job);
        return new("lucky-character", GameplayBenefitScope.For(context.Character.CharacterId), rules.QuestExperience(lucky, context.Quest));
    }
}
