using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageEngagementCharacterizationTests;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>The pre-wedding quest service counts pair clears on its own, without the marriage use cases behind it.</summary>
public sealed class MarriageQuestDomainServiceTests
{
    [Fact]
    public async Task Only_a_clear_by_exactly_the_engaged_pair_after_acceptance_counts_and_only_once_per_run()
    {
        var (f, peer) = await QuestWorldAsync(); await using var owned = f;
        var stranger = AddCharacter(f, 3, 73, 0);
        var strangerPartner = AddCharacter(f, 4, 74, 1);
        await EngageAsync(f, f.Session, peer);
        await AcceptTrialAsync(f, 1, 71); await AcceptTrialAsync(f, 2, 72);
        var quests = f.Services.GetRequiredService<MarriageQuestDomainService>();
        var valid = new DungeonMapCleared(DungeonRunId.New(), 2, DungeonDifficulty.Normal, new(0, 0), 100, 1, DateTimeOffset.UtcNow.AddSeconds(1),
            [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]) { IsBossRoom = true };
        async Task RecordAsync(DungeonMapCleared clear)
        {
            await using var tx = f.Scope.Begin();
            await quests.RecordTrialClearAsync(clear, default);
            await tx.CommitAsync(default);
        }
        int Clears() => f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears");
        int Remaining() => f.Scalar("SELECT SUM(trigger_value) FROM character_active_quests WHERE quest_id=20737");

        await RecordAsync(valid with { Participants = [new(new(1), new(71), 0, true)] });
        await RecordAsync(valid with { Participants = [new(new(1), new(71), 0, true), new(stranger.AccountId, stranger.CharacterId, 1, false)] });
        await EngageAsync(f, stranger, strangerPartner);
        await AcceptTrialAsync(f, 3, 73);
        await RecordAsync(valid with { Participants = [new(new(1), new(71), 0, true), new(stranger.AccountId, stranger.CharacterId, 1, false)] });
        await RecordAsync(valid with { Participants = [new(stranger.AccountId, stranger.CharacterId, 0, true), new(new(1), new(71), 1, false)] });
        await RecordAsync(valid with { ClearedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        Assert.Equal(0, Clears());
        Assert.Equal(15, Remaining());

        await RecordAsync(valid);
        Assert.Equal(2, Clears());
        Assert.Equal(13, Remaining());
        await RecordAsync(valid);
        Assert.Equal(2, Clears());
        Assert.Equal(13, Remaining());
    }
}
