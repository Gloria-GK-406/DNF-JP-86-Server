using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Accounts;

internal sealed class QuestHelperPuppetRuleService(IQuestHelperPuppetDefinitionSource source) : IQuestHelperPuppetRuleService, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lazy<IReadOnlyList<int>> items = new(source.ReadPuppetItemIds);
    public IReadOnlyList<int> PuppetItemIds => items.Value;
    public string Name => "quest-helper-puppets";
    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(items.Value.Count);
}
