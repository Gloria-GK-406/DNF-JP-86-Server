using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.GuardianAltar;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class GuardianAltarProtocolFlowTests
{
    [SkippableFact]
    public async Task Gold_reset_and_achievement_cross_the_real_command_boundary_and_stale_frames_cannot_repeat_assets()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services => services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        f.SetLevel(65);
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var rules = f.Services.GetRequiredService<IGuardianAltarRuleService>();
        var store = f.Services.GetRequiredService<IGuardianAltarProgressDomainStore>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = ((Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            var old = (await store.ReadAsync(f.Session.CharacterId, default))!; var unlocked = old;
            foreach (var stage in rules.Catalog.Stages.Take(4))
                unlocked = rules.RecordClear(unlocked, rules.Admission(unlocked, stage.DungeonId, GuardianAltarDifficulty.Easy)!);
            await store.SaveAsync(f.Session.CharacterId, unlocked with { Revision = old.Revision + 1 }, old.Revision, default);
            await tx.CommitAsync(default);
        });
        var character = f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterDomainService>().Get(f.Session.CharacterId);
        var gold = await character.GoldAsync(default);
        var reset = new byte[18];
        await EffectRequestTransport.SendRaw(f, new(1, 0x39A, 0, 8), reset);
        Assert.Equal(gold - 500, await character.GoldAsync(default));
        Assert.Contains(f.Sender.Outbound.Frames, x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x362);
        var frames = f.Sender.Outbound.Frames.Count;
        await EffectRequestTransport.SendRaw(f, new(1, 0x39A, 0, 8), reset);
        Assert.Equal(gold - 500, await character.GoldAsync(default)); Assert.Equal(frames, f.Sender.Outbound.Frames.Count);
        var claim = new byte[18]; BinaryPrimitives.WriteInt32LittleEndian(claim.AsSpan(14), 1);
        await EffectRequestTransport.SendRaw(f, new(1, 0x399, 0, 9), claim);
        Assert.Contains(1, (await altar.ReadProgressAsync(f.Session.CharacterId, default)).ClaimedAchievements);
        Assert.Equal(2, f.Count(2749213));
        await EffectRequestTransport.SendRaw(f, new(1, 0x399, 0, 10), claim);
        Assert.Equal(2, f.Count(2749213));
    }
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_selection_loading_failure_and_return_use_registered_commands_and_fence_stale_generation(bool finishLoading)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService, Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
        });
        f.SetLevel(65);
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 3511);
        await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, 8), body);
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var run = Assert.IsType<Dfo.Server.Abstractions.World.DungeonRunState>(runs.Find(f.Session.CharacterId));
        var types = f.Sender.Outbound.Frames.Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1))).ToArray();
        Assert.Contains((ushort)0x35B, types); Assert.Contains((ushort)0x35C, types);
        Assert.DoesNotContain((ushort)0x1D, types);
        var loading = altar.Find(run.RunId)!;
        if (finishLoading)
        {
            await EffectRequestTransport.SendRaw(f, new(1, 0x25, 0, 9), new byte[8]);
            Assert.Equal(GuardianAltarStatus.Fighting, altar.Find(run.RunId)!.Status);
            Assert.Contains(f.Sender.Outbound.Frames, x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x1E);
            var death = new byte[62];
            BinaryPrimitives.WriteUInt32LittleEndian(death, run.CurrentCellState.Actors.Single(x => x.Actor.Code == 64597).Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(death.AsSpan(4), f.Session.WireUserId);
            await EffectRequestTransport.SendRaw(f, new(1, 0x27, 0, 10), death);
            Assert.True(runs.Find(run.RunId)!.CurrentCellState.Actors.Single(x => x.Actor.Code == 64597).IsDead);
            f.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        else f.Clock.Advance(TimeSpan.FromMinutes(2));
        var commands = f.Services.GetRequiredService<ICommandBus>();
        InternalCommandOrigin Origin() => new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "test:guardian-deadline", InternalCommandCancellation.BodyOnly);
        Assert.True(await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(altar.Find(run.RunId)!), Origin()));
        var lost = altar.Find(run.RunId)!; Assert.Equal(GuardianAltarStatus.Lost, lost.Status);
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(loading), Origin()));
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin()));
        f.Clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(f.Session, Assert.Single(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin())));
        Assert.Null(runs.Find(f.Session.CharacterId));
        Assert.True(await commands.SendInternalAsync(new Dfo.Server.Protocol.Game.ProjectDungeonReturnCommand(f.Session, run.RunId, false), Origin()));
        Assert.True(f.Session.Owns(f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!));
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin()));
    }
    [SkippableTheory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    public async Task Native_open_runs_the_registered_decoder_command_transaction_and_encoder(int level, bool allowed)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services => services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        f.SetLevel(checked((byte)level));
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var frame = Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal((ushort)0x392, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(1, frame[15]); Assert.Equal(allowed ? 0 : 1, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16)));
        Assert.Equal(allowed ? 1 : 0, f.Scalar("SELECT count(*) FROM character_guardian_altar"));
        Assert.Equal(allowed, f.Services.GetRequiredService<IGuardianAltarDomainService>().IsSelecting(f.Session));
    }
    [SkippableFact]
    public async Task Packed_slot_action_changes_only_owned_progress_and_insufficient_star_purchase_is_refused()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services => services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        f.SetLevel(65);
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var slot = new byte[28]; BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(14), 1); slot[18] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(19), 1); slot[23] = 0; BinaryPrimitives.WriteInt32LittleEndian(slot.AsSpan(24), 2000);
        await EffectRequestTransport.SendRaw(f, new(1, 0x394, 0, 8), slot);
        var response = f.Sender.Outbound.Frames.Last();
        Assert.Equal((ushort)0x394, BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(1)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(16)));
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var progress = await altar.ReadProgressAsync(f.Session.CharacterId, default);
        Assert.Equal(2000, progress.Slots.Single(x => x.Index == 0).Id);
        var buy = new byte[30]; BinaryPrimitives.WriteInt32LittleEndian(buy.AsSpan(14), 1);
        BinaryPrimitives.WriteInt32LittleEndian(buy.AsSpan(22), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(buy.AsSpan(26), 2); BinaryPrimitives.WriteInt16LittleEndian(buy.AsSpan(28), 2);
        await EffectRequestTransport.SendRaw(f, new(1, 0x393, 0, 9), buy);
        response = f.Sender.Outbound.Frames.Last();
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(16)));
        Assert.Equal(progress.Revision, (await altar.ReadProgressAsync(f.Session.CharacterId, default)).Revision);
    }
}
