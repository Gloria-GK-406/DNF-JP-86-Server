using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Accounts;

internal sealed class QuestHelperPuppetRuleService(IQuestHelperPuppetDefinitionSource source) : IQuestHelperPuppetRuleService
{
    private readonly Lazy<IReadOnlyList<int>> items = new(source.ReadPuppetItemIds);
    public IReadOnlyList<int> PuppetItemIds => items.Value;
}
