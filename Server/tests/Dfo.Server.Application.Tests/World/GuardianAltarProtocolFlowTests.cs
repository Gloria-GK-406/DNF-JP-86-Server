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
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Character_death_anchors_failure_to_the_event_and_emits_only_one_death_eplp_without_rewards(bool paused)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService, Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
        });
        await f.Seed(1, 1, 10, 0);
        f.SetLevel(65);
        await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var entry = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(entry, 3511); entry[5] = 3;
        await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, 8), entry);
        await EffectRequestTransport.SendRaw(f, new(1, 0x25, 0, 9), new byte[8]);
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var run = f.Services.DungeonRuns().Find(f.Session.CharacterId)!;
        var commands = f.Services.GetRequiredService<ICommandBus>();
        InternalCommandOrigin Origin() => new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "test:guardian-death", InternalCommandCancellation.BodyOnly);
        var stale = altar.Find(run.RunId)!;
        var beforeProgress = await altar.ReadProgressAsync(f.Session.CharacterId, default);
        var beforeItems = f.Scalar("SELECT count(*) FROM items");
        var before = f.Sender.Outbound.Frames.Count;
        ushort sequence = 10;
        f.Clock.Advance(TimeSpan.FromMilliseconds(750));
        if (paused)
        {
            var pause = new byte[15]; pause[14] = 1;
            await EffectRequestTransport.SendRaw(f, new(1, 0x398, 0, sequence++), pause);
            before = f.Sender.Outbound.Frames.Count;
        }
        await EffectRequestTransport.SendRaw(f, new(1, 0x28, 0, sequence++), new byte[4]);
        var deathAt = f.Clock.GetUtcNow();
        ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
        Assert.Equal(new ushort[] { 0x28, 0x20 }, f.Sender.Outbound.Frames.Skip(before).Select(Type));
        f.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(altar.Find(run.RunId)!), Origin());
        Assert.DoesNotContain(f.Sender.Outbound.Frames.Skip(before), x => Type(x) is 0x360 or 0x361);
        var pending = altar.Find(run.RunId)!;
        Assert.Equal(deathAt.AddSeconds(1), pending.Deadline);
        await EffectRequestTransport.SendRaw(f, new(1, 0x28, 0, sequence++), new byte[4]);
        Assert.Equal(pending.Generation, altar.Find(run.RunId)!.Generation);
        var revive = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(revive, f.Session.WireUserId);
        await EffectRequestTransport.SendRaw(f, new(1, 0x29, 0, sequence++), revive);
        Assert.Equal(0, f.Sender.Outbound.Frames.Last()[15]);
        var coin = await f.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>()
            .FindContainer(f.Session.CharacterId, Dfo.Server.Data.Inventory.InventoryContainerType.Main)!.GetAsync(1, default);
        Assert.Equal(10, coin!.Snapshot.ToCore().Value);
        Assert.Contains(f.Session.CharacterId, f.Services.DungeonRuns().Find(run.RunId)!.DeadParticipants);
        Assert.Equal(pending.Generation, altar.Find(run.RunId)!.Generation);
        f.Clock.Advance(TimeSpan.FromMilliseconds(749));
        await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(pending), Origin());
        Assert.DoesNotContain(f.Sender.Outbound.Frames.Skip(before), x => Type(x) is 0x360 or 0x361);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(altar.Find(run.RunId)!), Origin());
        var ended = altar.Find(run.RunId)!;
        Assert.Equal(GuardianAltarStatus.Lost, ended.Status);
        Assert.Equal(deathAt.AddSeconds(51), ended.Deadline);
        Assert.DoesNotContain(f.Sender.Outbound.Frames.Skip(before), x => Type(x) == 0x361);
        var eplp = Assert.Single(f.Sender.Outbound.Frames.Skip(before), x => Type(x) == 0x360);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 1 }, eplp[15..]);
        var count = f.Sender.Outbound.Frames.Count;
        await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(stale), Origin());
        await commands.SendInternalAsync(new AdvanceGuardianAltarCommand(pending), Origin());
        Assert.Equal(count, f.Sender.Outbound.Frames.Count);
        var afterProgress = await altar.ReadProgressAsync(f.Session.CharacterId, default);
        Assert.Equal((beforeProgress.Revision, beforeProgress.FreeTickets, beforeProgress.ExtraTickets, beforeProgress.GameStars),
            (afterProgress.Revision, afterProgress.FreeTickets, afterProgress.ExtraTickets, afterProgress.GameStars));
        Assert.Equal(beforeItems, f.Scalar("SELECT count(*) FROM items"));
        Assert.Equal(0, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(ended), Origin()));
        f.Clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(f.Session, Assert.Single(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(ended), Origin())));
        Assert.True(await commands.SendInternalAsync(new Dfo.Server.Protocol.Game.ProjectDungeonReturnCommand(f.Session, run.RunId, false), Origin()));
        Assert.Null(f.Services.DungeonRuns().Find(f.Session.CharacterId));
    }
    [SkippableFact]
    public void Cleared_difficulty_unlocks_the_next_map_without_treating_its_combat_basis_as_a_star_cost()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services => services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }));
        var rules = f.Services.GetRequiredService<IGuardianAltarRuleService>();
        var initial = rules.Initial(new(2026, 10, 28));
        var cleared = rules.RecordClear(initial, rules.Admission(initial, 3511, GuardianAltarDifficulty.Easy)!);
        Assert.Equal(3, cleared.GameStars);
        var medium = Assert.IsType<GuardianAltarAdmission>(rules.Admission(cleared, 3511, GuardianAltarDifficulty.Medium));
        Assert.Equal(10, medium.BasisLevel);
        Assert.Null(rules.Admission(initial, 3511, GuardianAltarDifficulty.Medium));
    }
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
    [InlineData(false, (byte)0)]
    [InlineData(true, (byte)0)]
    [InlineData(false, (byte)3)]
    [InlineData(true, (byte)3)]
    public async Task Native_selection_loading_failure_and_return_use_registered_commands_and_fence_stale_generation(bool finishLoading, byte requestMode)
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
        body[5] = requestMode;
        await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, 8), body);
        var runs = f.Services.DungeonRuns();
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var run = Assert.IsType<Dfo.Server.Abstractions.World.DungeonRunState>(runs.Find(f.Session.CharacterId));
        var types = f.Sender.Outbound.Frames.Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1))).ToArray();
        Assert.Contains((ushort)0x35B, types); Assert.Contains((ushort)0x35C, types);
        Assert.DoesNotContain((ushort)0x1D, types);
        var mapFrame = Assert.Single(f.Sender.Outbound.Frames, x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x35C);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(mapFrame.AsSpan(19)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(mapFrame.AsSpan(31)));
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
        if (finishLoading) Assert.Equal(GuardianAltarEndReason.CenterDestroyed, lost.EndReason);
        Assert.DoesNotContain(f.Sender.Outbound.Frames, x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x361);
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(loading), Origin()));
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin()));
        f.Clock.Advance(TimeSpan.FromSeconds(50));
        var beforeReturn = f.Sender.Outbound.Frames.Count;
        Assert.Equal(f.Session, Assert.Single(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin())));
        Assert.Null(runs.Find(f.Session.CharacterId));
        Assert.True(await commands.SendInternalAsync(new Dfo.Server.Protocol.Game.ProjectDungeonReturnCommand(f.Session, run.RunId, false), Origin()));
        Assert.Equal(f.Session, f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Session);
        var returnTypes = f.Sender.Outbound.Frames.Skip(beforeReturn).Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1))).ToList();
        Assert.Contains((ushort)0x0D, returnTypes);
        Assert.True(returnTypes.IndexOf(0x0D) < returnTypes.IndexOf(0x17));
        Assert.Empty(await commands.SendInternalAsync(new ReturnGuardianAltarCommand(lost), Origin()));
    }
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_exit_restores_inventory_before_town_and_next_entry_initializes_the_client(bool giveUp)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService, Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
        });
        f.SetLevel(65);
        ushort sequence = 7;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, sequence++), []);
            var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 3511); body[5] = 3;
            await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, sequence++), body);
            var mapFrame = f.Sender.Outbound.Frames.Last(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x35C);
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(mapFrame.AsSpan(19)));
            await EffectRequestTransport.SendRaw(f, new(1, 0x25, 0, sequence++), new byte[8]);
            var beforeReturn = f.Sender.Outbound.Frames.Count;
            await EffectRequestTransport.SendRaw(f, new(1, giveUp ? (ushort)0x2A : (ushort)0x48, 0, sequence++), giveUp ? [] : [1, 1]);
            var types = f.Sender.Outbound.Frames.Skip(beforeReturn).Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1))).ToList();
            Assert.Null(f.Services.DungeonRuns().Find(f.Session.CharacterId));
            Assert.Contains((ushort)0x0D, types);
            Assert.True(types.IndexOf(0x0D) < types.IndexOf(0x17));
        }
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
    [SkippableTheory]
    [InlineData(false, (byte)3, (byte)0)]
    [InlineData(true, (byte)1, (byte)0)]
    [InlineData(true, (byte)2, (byte)0)]
    [InlineData(true, (byte)4, (byte)0)]
    [InlineData(true, (byte)255, (byte)0)]
    [InlineData(true, (byte)3, (byte)1)]
    public async Task Invalid_entry_preserves_progress_and_refuses_without_upgrade_room_error(bool open, byte mode, byte practice)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var f = new ShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
        });
        f.SetLevel(65);
        if (open) await EffectRequestTransport.SendRaw(f, new(1, 0x392, 0, 7), []);
        var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
        var before = await altar.ReadProgressAsync(f.Session.CharacterId, default);
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 3511);
        body[5] = mode; body[6] = practice;
        for (ushort sequence = 8; sequence < 10; sequence++)
        {
            await EffectRequestTransport.SendRaw(f, new(1, 0x10, 0, sequence), body);
            var refusal = f.Sender.Outbound.Frames.Last();
            Assert.Equal((ushort)0x10, BinaryPrimitives.ReadUInt16LittleEndian(refusal.AsSpan(1)));
            Assert.Equal(new byte[] { 0, 0x13, 0 }, refusal[15..]);
            Assert.Null(f.Services.DungeonRuns().Find(f.Session.CharacterId));
            var after = await altar.ReadProgressAsync(f.Session.CharacterId, default);
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal((before.FreeTickets, before.ExtraTickets, before.AvailableStars, before.GameStars, before.PurchasedStars),
                (after.FreeTickets, after.ExtraTickets, after.AvailableStars, after.GameStars, after.PurchasedStars));
            Assert.Equal(before.Stages.OrderBy(x => x.Key), after.Stages.OrderBy(x => x.Key));
            Assert.Equal(before.Purchases.ToArray(), after.Purchases.ToArray());
        }
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
