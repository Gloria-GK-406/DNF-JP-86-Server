using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTypedItemUseTests
{
    [Theory]
    [InlineData("absent", true)]
    [InlineData("fatigue-action", true)]
    [InlineData("duration-target", true)]
    [InlineData("wrong-scalar", false)]
    [InlineData("empty-scalar", false)]
    [InlineData("extra-scalar", false)]
    [InlineData("duplicate-scalar", false)]
    [InlineData("integer-rate", false)]
    [InlineData("infinite-rate", false)]
    [InlineData("invalid-duration-target", false)]
    [InlineData("duplicate-type", false)]
    [InlineData("misordered-type", false)]
    [InlineData("mixed-jobs", false)]
    public async Task Production_read_distinguishes_absence_from_invalid_declarations(string shape, bool readable)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst").Int(1).Quoted("test.stk").End();
        var script = builder.AddScript("stackable", "test.stk");
        switch (shape)
        {
            case "fatigue-action": script.Tag("[action type]").Quoted("[add fatigue]").Int(42); break;
            case "duration-target": script.Tag("[exp bonus rate]").Single(1)
                .Tag("[stat change duration]").Int(7200000).Quoted("myself"); break;
            case "wrong-scalar": script.Tag("[cool time]").Quoted("100"); break;
            case "empty-scalar": script.Tag("[cool time]"); break;
            case "extra-scalar": script.Tag("[cool time]").Int(100).Int(200); break;
            case "duplicate-scalar": script.Tag("[cool time]").Int(100).Tag("[cool time]").Int(200); break;
            case "integer-rate": script.Tag("[exp bonus rate]").Int(1).Tag("[stat change duration]").Int(100); break;
            case "infinite-rate": script.Tag("[exp bonus rate]").Single(float.PositiveInfinity); break;
            case "invalid-duration-target": script.Tag("[stat change duration]").Int(100).Int(200); break;
            case "duplicate-type": script.Tag("[stackable type]").Quoted("[unlimited]")
                .Tag("[stackable type]").Quoted("[usable]"); break;
            case "misordered-type": script.Tag("[stackable type]").Int(1).Quoted("[unlimited]"); break;
            case "mixed-jobs": script.Tag("[usable job]").Quoted("[all]").Int(1); break;
        }
        script.End();
        var path = Path.Combine(Path.GetTempPath(), $"typed-use-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var rule = await new PvfItemUseRuleDefinitionSource(provider).GetAsync(1, default);
            Assert.NotNull(rule);
            Assert.Equal(readable, rule.IsReadable);
            if (shape == "fatigue-action")
            {
                var action = await new PvfStackableActionDefinitionSource(provider).GetAsync(1, default);
                Assert.NotNull(action);
                Assert.True(action.IsReadable);
                Assert.Equal(42, action.FatigueRecovery);
            }
            if (shape == "absent") Assert.Equal(0, rule.CooldownMilliseconds);
            if (shape == "duration-target") Assert.Equal((1d, 7200000), (rule.ExpBonusRate, rule.EffectDurationMilliseconds));
        }
        finally { File.Delete(path); }
    }
}
