using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class WeaknessTests
{
    [Fact]
    public async Task Failed_level_read_is_retried_after_exit_even_after_entering_a_new_run()
    {
        var character = new CurrentCharacterStub { ProgressReader = () => throw new IOException("transient") };
        var weakness = new WeaknessDomainService(() => CurrentCharacterStub.Domain(character),
            new WeaknessRuleService(new ConfigurationBuilder().Build()));
        var runs = new DungeonRunDomainService(null!, null!, weakness);
        var old = runs.Begin(Id, Layout, DungeonDifficulty.Normal);
        Assert.True(await runs.EndAsync(Id, old.RunId));
        Assert.Null(runs.Find(Id));
        Assert.Equal(100, weakness.For(Id).Value);
        var next = runs.Begin(Id, Layout, DungeonDifficulty.Normal);
        character.ProgressReader = null;
        Assert.Equal([Id], await weakness.RetryPendingWeakness(default));
        Assert.Equal(10, weakness.For(Id).Value);
        Assert.Empty(weakness.AdvanceRecovery());
        Assert.False(await runs.EndAsync(Id, old.RunId));
        Assert.Equal(next.RunId, runs.Find(Id)!.RunId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_old_read_cannot_override_recover_or_a_new_failure(bool newFailure)
    {
        var read = new TaskCompletionSource<CharacterProgressionState?>();
        var character = new CurrentCharacterStub { ProgressReader = () => new(read.Task) };
        var weakness = new WeaknessDomainService(() => CurrentCharacterStub.Domain(character),
            new WeaknessRuleService(new ConfigurationBuilder().Build()));
        var pending = weakness.For(Id).SetWeakness().AsTask();
        character.ProgressReader = null;
        if (newFailure)
        {
            await weakness.For(Id).SetWeakness();
            weakness.AdvanceRecovery();
        }
        else weakness.For(Id).Recover();
        read.SetResult(new(20, 0));
        await pending;
        Assert.Equal(newFailure ? 19 : 100, weakness.For(Id).Value);
        Assert.Empty(await weakness.RetryPendingWeakness(default));
    }

    [Fact]
    public async Task One_party_members_failed_read_does_not_block_exit_or_lose_its_penalty()
    {
        var second = new CurrentCharacterStub { ProgressReader = () => throw new IOException("transient") };
        var weakness = new WeaknessDomainService(() => new CharacterDomainService(null!, null!,
                id => id == Id ? new CurrentCharacterStub() : second, null!),
            new WeaknessRuleService(new ConfigurationBuilder().Build()));
        var runs = new DungeonRunDomainService(null!, null!, weakness);
        DungeonRunParticipant[] members = [new(new(1), Id, 0, true), new(new(2), new(2), 1, false)];
        var run = runs.PreparePartyEntry(members, Layout, DungeonDifficulty.Normal).Run;
        foreach (var member in members)
        {
            runs.TryMarkParticipantReady(member.CharacterId, run.RunId, run.RoomVersion);
            runs.TryMarkParticipantDead(member.CharacterId, run.RunId, run.RoomVersion);
        }
        runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
        Assert.NotNull(await runs.TryEndPartyWipeAsync(run.RunId, DateTimeOffset.UnixEpoch.AddSeconds(10)));
        Assert.All(members, member => Assert.Null(runs.Find(member.CharacterId)));
        Assert.Equal(10, weakness.For(Id).Value);
        Assert.Equal(100, weakness.For(new(2)).Value);
        second.ProgressReader = null;
        var command = new Dfo.Server.Protocol.Game.Weakness.AdvanceWeaknessRecoveryCommandHandler(weakness);
        var changed = await command.HandleAsync(null!, new(), default);
        Assert.Equal(2, changed.Count);
        Assert.Equal(19, weakness.For(Id).Value);
        Assert.Equal(10, weakness.For(new(2)).Value);
        Assert.Empty(await weakness.RetryPendingWeakness(default));
    }

    private static readonly CharacterId Id = new(1);
    private static WeaknessDomainService Create(byte level = 20, Dictionary<string, string?>? config = null) =>
        new(() => CurrentCharacterStub.Domain(new() { Id = Id, Progress = new(level, 0) }),
            new WeaknessRuleService(new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build()));

    [Theory]
    [InlineData(19, 100)]
    [InlineData(20, 10)]
    [InlineData(85, 10)]
    public async Task Level_threshold_is_applied_inside_set(byte level, byte expected)
    {
        var service = Create(level);
        await service.For(Id).SetWeakness();
        Assert.Equal(expected, service.For(Id).Value);
    }

    [Fact]
    public async Task Ten_ticks_recover_offline_characters_and_remove_completed_entries()
    {
        var service = Create();
        Assert.Equal(TimeSpan.FromSeconds(60), service.RecoveryInterval);
        await service.For(Id).SetWeakness();
        for (var tick = 1; tick <= 10; tick++)
        {
            Assert.Equal([Id], service.AdvanceRecovery());
            Assert.Equal(10 + tick * 9, service.For(Id).Value);
        }
        Assert.Empty(service.AdvanceRecovery());
        Assert.Equal(100, Create().For(Id).Value);
    }

    [Fact]
    public async Task Pause_resume_and_full_recovery_are_idempotent()
    {
        var service = Create();
        var state = service.For(Id);
        state.PauseRecovery(); state.ResumeRecovery();
        Assert.Empty(service.AdvanceRecovery());
        await state.SetWeakness();
        state.PauseRecovery(); state.PauseRecovery();
        Assert.Empty(service.AdvanceRecovery());
        Assert.Equal(10, state.Value);
        state.ResumeRecovery(); state.ResumeRecovery();
        service.AdvanceRecovery();
        Assert.Equal(19, state.Value);
        state.Recover(); state.Recover();
        Assert.Equal(100, state.Value);
        Assert.Empty(service.AdvanceRecovery());
    }

    [Fact]
    public async Task Configuration_changes_initial_value_and_tick_with_clamping()
    {
        var service = Create(5, new()
        {
            ["Weakness:MinimumLevel"] = "5", ["Weakness:InitialValue"] = "32",
            ["Weakness:RecoveryIncrement"] = "40", ["Weakness:RecoveryIntervalSeconds"] = "2",
        });
        await service.For(Id).SetWeakness();
        Assert.Equal(32, service.For(Id).Value);
        Assert.Equal(TimeSpan.FromSeconds(2), service.RecoveryInterval);
        service.AdvanceRecovery(); Assert.Equal(72, service.For(Id).Value);
        service.AdvanceRecovery(); Assert.Equal(100, service.For(Id).Value);
        Assert.Empty(service.AdvanceRecovery());
    }

    [Theory]
    [InlineData("MinimumLevel", "0")]
    [InlineData("InitialValue", "101")]
    [InlineData("RecoveryIncrement", "0")]
    [InlineData("RecoveryIntervalSeconds", "0")]
    public void Invalid_configuration_is_rejected(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Create(config: new() { ["Weakness:" + key] = value }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exit_applies_once_and_clear_exit_does_not_apply(bool cleared)
    {
        var weakness = Create();
        var runs = new DungeonRunDomainService(null!, null!, weakness);
        var run = runs.Begin(Id, Layout, DungeonDifficulty.Normal);
        if (cleared) runs.TryOpenClear(Id, run.RunId, run.RoomVersion);
        Assert.True(await runs.EndAsync(Id, run.RunId));
        Assert.Equal(cleared ? 100 : 10, weakness.For(Id).Value);
        weakness.AdvanceRecovery();
        Assert.False(await runs.EndAsync(Id, run.RunId));
        Assert.Equal(cleared ? 100 : 19, weakness.For(Id).Value);
    }

    [Fact]
    public async Task Entry_pauses_existing_weakness_abort_resumes_and_old_exit_cannot_penalize_new_run()
    {
        var weakness = Create();
        await weakness.For(Id).SetWeakness();
        var runs = new DungeonRunDomainService(null!, null!, weakness);
        var old = runs.Begin(Id, Layout, DungeonDifficulty.Normal);
        Assert.Empty(weakness.AdvanceRecovery());
        var current = runs.Begin(Id, Layout, DungeonDifficulty.Normal);
        Assert.False(await runs.EndAsync(Id, old.RunId));
        Assert.Equal(10, weakness.For(Id).Value);
        Assert.Empty(weakness.AdvanceRecovery());
        Assert.True(runs.TryAbortEntry(current.RunId, current.EntryRevision, [Id]));
        weakness.AdvanceRecovery();
        Assert.Equal(19, weakness.For(Id).Value);
    }

    [Fact]
    public async Task Party_wipe_applies_to_every_participant_once()
    {
        var weakness = Create();
        var runs = new DungeonRunDomainService(null!, null!, weakness);
        DungeonRunParticipant[] members = [new(new(1), Id, 0, true), new(new(2), new(2), 1, false)];
        var run = runs.PreparePartyEntry(members, Layout, DungeonDifficulty.Normal).Run;
        foreach (var member in members)
        {
            runs.TryMarkParticipantReady(member.CharacterId, run.RunId, run.RoomVersion);
            runs.TryMarkParticipantDead(member.CharacterId, run.RunId, run.RoomVersion);
        }
        var now = DateTimeOffset.UnixEpoch;
        Assert.Empty(runs.FindExpiredPartyWipes(now));
        Assert.NotNull(await runs.TryEndPartyWipeAsync(run.RunId, now.AddSeconds(10)));
        Assert.All(members, member => Assert.Equal(10, weakness.For(member.CharacterId).Value));
        weakness.AdvanceRecovery();
        Assert.Null(await runs.TryEndPartyWipeAsync(run.RunId, now.AddSeconds(20)));
        Assert.All(members, member => Assert.Equal(19, weakness.For(member.CharacterId).Value));
    }

    private static readonly DungeonLayout Layout = new(10000, 0, 1, 1,
        new(0, 0), new(0, 0), 61000, 1, new Dictionary<MazeCell, int> { [new(0, 0)] = 61000 });
}
