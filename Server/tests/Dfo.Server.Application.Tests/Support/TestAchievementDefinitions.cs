using Dfo.Server.Abstractions.Achievements;

namespace Dfo.Server.Application.Tests.Support;

// Inventory/lifecycle fixtures do not exercise achievement content.
internal sealed class TestAchievementDefinitions : IAchievementDefinitionSource, ITitleBookDefinitionSource
{
    public IReadOnlyList<AchievementDefinition> Load() => [];
    IReadOnlyList<TitleBookCategoryDefinition> ITitleBookDefinitionSource.Load() => [];
}
