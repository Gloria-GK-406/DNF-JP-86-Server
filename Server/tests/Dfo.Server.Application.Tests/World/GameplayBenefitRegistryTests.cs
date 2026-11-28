using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Application.Benefits;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class GameplayBenefitRegistryTests
{
    [Fact]
    public async Task Replacement_is_whole_ordered_time_bound_and_snapshot_isolated()
    {
        await TestGameplay.RunAsync(VerifyReplacementAsync);
    }

    private static async Task VerifyReplacementAsync()
    {
        var registry = new GameplayBenefitRegistry();
        var global = GameplayBenefitScope.Global;
        var character = GameplayBenefitScope.For(new CharacterId(1));
        var stranger = GameplayBenefitScope.For(new CharacterId(2));
        var at = DateTimeOffset.UnixEpoch.AddDays(1);
        var query = new GameplayBenefitsQuery<int>(registry, [], TimeProvider.System);
        Assert.True(registry.Replace(new("world", global,
            new GameplayBenefit("b", "global bonus", 40, new Effect(4))), 1));
        Assert.True(registry.Replace(new("event", character,
            new GameplayBenefit("a", "always active", 20, new Effect(2)),
            new GameplayBenefit("timed", "ends tomorrow", 10, new Effect(1), at, at.AddDays(1)),
            new GameplayBenefit("future", "starts tomorrow", 30, new Effect(3), at.AddDays(1))), 3));
        Assert.True(registry.Replace(new("event", stranger, new GameplayBenefit("other", "other character", 0, new Effect(0))), 1));
        var frozen = await query.ReadAtAsync(0, at, default, character, global);
        Assert.Equal([1, 2, 4], frozen.Effects<Effect>().Select(effect => effect.Value));
        Assert.Equal("event", frozen.Active[0].Source);
        Assert.Equal(character, frozen.Active[0].Scope);
        Assert.DoesNotContain(frozen.Declarations, batch => batch.Scope == stranger);
        Assert.Equal([2, 3, 4], (await query.ReadAtAsync(0, at.AddDays(1), default, global, character))
            .Effects<Effect>().Select(effect => effect.Value));

        Assert.True(registry.Replace(new("event", character), 4));
        Assert.False(registry.Replace(new("event", character, new GameplayBenefit("stale", "stale", 0, new Effect(9))), 3));
        var cleared = await query.ReadAtAsync(0, at, default, character, global);
        Assert.Empty(Assert.Single(cleared.Declarations, batch => batch.Source == "event").Benefits);
        Assert.Equal(4, Assert.Single(cleared.Effects<Effect>()).Value);
        Assert.Equal([1, 2, 4], frozen.Effects<Effect>().Select(effect => effect.Value));
    }

    private readonly record struct Effect(int Value) : IGameplayBenefitEffect;
}
