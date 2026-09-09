using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterStatRuleServiceTests
{
    [SkippableFact]
    public async Task Real_archive_applies_the_selected_growth_after_level_fifteen()
    {
        var options = new PvfContentOptions();
        Skip.If(options.ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(options);
        var rules = new CharacterStatRuleService(new PvfCharacterStatDefinitionSource(provider));
        await rules.InitializeAsync();
        // Independent arithmetic from Swordman.chr: initial 180, unadvanced 45, growtype 2 HP 50.
        Assert.Equal(8600, rules.GetStats(new CharacterCareer(0, 1), 16).HpMax);
    }
}
