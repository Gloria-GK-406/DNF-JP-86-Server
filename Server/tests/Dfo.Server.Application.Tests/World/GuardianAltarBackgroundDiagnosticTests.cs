using System.Buffers.Binary;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.GuardianAltar;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.World;

public sealed class GuardianAltarBackgroundDiagnosticTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Hosted_timer_emits_first_enemy_without_manually_submitting_advance()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService,
                Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
        });
        f.SetLevel(65);
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 3511); body[5] = 3;
        await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, 8), body);
        await EffectRequestTransport.SendRaw(f, new(1, 0x25, 0, 9), new byte[8]);
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var run = runs.Find(f.Session.CharacterId)!;
        var initial = altar.Find(run.RunId)!;
        Assert.Equal(GuardianAltarStatus.Fighting, initial.Status);
        Assert.NotEmpty(initial.PlannedSpawns);
        output.WriteLine($"Initial generation={initial.Generation} planned={initial.PlannedSpawns.Length} firstSpawnMs={initial.PlannedSpawns[0].OffsetMilliseconds}");
        var schedulerType = typeof(AdvanceGuardianAltarCommand).Assembly.GetType(
            "Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarScheduler", throwOnError: true)!;
        var scheduler = (IHostedService)f.Services.GetRequiredService(schedulerType);
        await scheduler.StartAsync(default);
        try
        {
            f.Clock.Advance(TimeSpan.FromMilliseconds(initial.PlannedSpawns[0].OffsetMilliseconds + 1000));
            f.Services.GetRequiredService<IGuardianAltarSchedulerWakeSignal>().Wake();
            var until = DateTime.UtcNow.AddSeconds(5);
            // A committed generation precedes its outbox projection; wait for the observable notice.
            while (DateTime.UtcNow < until && !f.Sender.Outbound.Frames.Any(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x35D))
                await Task.Delay(25);
        }
        finally { await scheduler.StopAsync(default); }
        var current = altar.Find(run.RunId)!;
        var frames = f.Sender.Outbound.Frames.ToArray();
        var spawnCount = frames.Count(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x35D);
        output.WriteLine($"After hosted timer generation={current.Generation} phase={current.Phase} elapsedMs={current.PhaseElapsedMilliseconds} actors={runs.Find(run.RunId)!.CurrentCellState.Actors.Count} enemyNotices={spawnCount}");
        Assert.True(current.Generation > initial.Generation);
        Assert.True(spawnCount > 0);
    }
}
