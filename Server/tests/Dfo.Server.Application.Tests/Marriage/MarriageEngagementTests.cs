using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageEngagementTests
{
    private sealed class Content : IMarriageProposalContentSource { public MarriageProposalContent Load() => new(30); }
    private sealed class Parameters : IQuestParameterSource
    {
        public QuestParameters Load() => new(Enumerable.Repeat(0, 100).ToArray(), new Dictionary<char, int>(), 100, 100, 100, 100);
    }
    private sealed class Definitions : IQuestDefinitionSource
    {
        private static QuestDefinition Quest(int id, string type, IReadOnlyList<IReadOnlyList<int>> previous) =>
            new(id, "wedding", QuestGrade.Wedding, null, new(30, 99), null, 20013, 20013, [], type, "",
                true, false, null, null, null, null, null, previous, [], [], []);
        public static QuestDefinition Trial => Quest(20737, "condition under clear", [])
            with { ObjectiveSubType = 22, ObjectiveArguments = [-1, -1, 5] };
        public static QuestDefinition Material => Quest(20738, "seeking", [new[] { 20737 }])
            with { SeekingItems = [new(10002610, 1)] };
        public ValueTask<QuestDefinition?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<QuestDefinition?>(id == 20737 ? Trial : id == 20738 ? Material : id == 20999 ? Quest(id, "seeking", []) : null);
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Trial, Material, Quest(20999, "seeking", [])]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }
    private sealed class Dungeons : IMarriageDungeonContentSource
    {
        public ValueTask<bool> IsOrdinaryMapAsync(int dungeon, int map, CancellationToken ct) => ValueTask.FromResult(dungeon == 2 && map == 100);
    }
    private static Task<(LearningFixture F, Dfo.Server.Data.World.SelectedSessionIdentity Peer)> Fixture(bool realContent = false) =>
        MarriageProposalTests.Fixture(configure: s =>
        {
            if (!realContent)
            {
                s.AddSingleton<IQuestDefinitionSource, Definitions>();
                s.AddSingleton<IQuestParameterSource, Parameters>();
            }
            s.AddSingleton<IMarriageDungeonContentSource, Dungeons>();
            s.AddCommandHandler<AcceptQuestCommand, QuestAcceptOutcome, AcceptQuestCommandHandler>(CommandExecutionKind.Transactional);
        });
    private static async Task Engage(LearningFixture f, Dfo.Server.Data.World.SelectedSessionIdentity peer)
    {
        Assert.True((await MarriageProposalTests.Run(f, x => x.ProposeAsync(f.Session, peer, default))).Succeeded);
        Assert.True((await MarriageProposalTests.Run(f, x => x.RespondProposalAsync(peer, f.Session, 0, default))).Succeeded);
    }
    private static async Task Accept(LearningFixture f, int account, int character)
    {
        var result = await f.Services.GetRequiredService<ICommandBus>().SendAsync(new AcceptQuestCommand(new(account), new(character), 20737), default);
        Assert.True(result.Accepted);
        Assert.Equal(5u, result.InitialTrigger);
    }
    [Fact]
    public async Task Pending_has_no_menu_state_and_cannot_accept_wedding_by_raw_ID()
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        await MarriageProposalTests.Run(f, x => x.ProposeAsync(f.Session, peer, default));
        Assert.False((await f.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(new(1), new(71), default)).IsEngaged);
        Assert.False((await f.Services.GetRequiredService<ICommandBus>().SendAsync(new AcceptQuestCommand(new(1), new(71), 20737), default)).Accepted);
        Assert.Empty((await f.Services.GetRequiredService<IQuestQueryDomainService>().ReadAvailableAsync(new(1), new(71), default)).Quests);
    }
    [Fact]
    public async Task Engagement_offers_only_first_trial_and_restores_both_profiles_after_session_loss_and_database_reopen()
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        await Engage(f, peer);
        var state = f.Services.GetRequiredService<IMarriageQueryDomainService>();
        var first = await state.SnapshotAsync(new(1), new(71), default);
        var second = await state.SnapshotAsync(new(2), new(72), default);
        Assert.Equal(first.RelationId, second.RelationId);
        Assert.Equal(72, first.Partner!.CharacterId); Assert.Equal(71, second.Partner!.CharacterId);
        Assert.False((await state.SnapshotAsync(new(2), new(71), default)).IsEngaged);
        Assert.Equal(new[] { 20737 }, (await f.Services.GetRequiredService<IQuestQueryDomainService>().ReadAvailableAsync(new(1), new(71), default)).Quests.Select(x => x.QuestId));
        await using var reopened = new LearningFixture(existing: f.Path);
        Assert.True((await reopened.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(new(1), new(71), default)).IsEngaged);
        await Accept(f, 1, 71);
        Assert.Empty((await f.Services.GetRequiredService<IQuestQueryDomainService>().ReadAvailableAsync(new(1), new(71), default)).Quests);
    }
    [Fact]
    public async Task Five_distinct_ordinary_pair_boss_clears_are_durable_and_client_reports_cannot_finish_trial()
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        await Engage(f, peer); await Accept(f, 1, 71); await Accept(f, 2, 72);
        var wedding = f.Services.GetRequiredService<IMarriageQuestDomainService>();
        Assert.False(await wedding.CanCompleteAsync(new(1), new(71), 20737, default));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                var report = await f.Services.GetRequiredService<IQuestProgressDomainService>().ReportAsync(new(1), new(71),
                    [new ClientQuestTriggerReport(20737, 0, false)], default);
                Assert.Equal(5, (await f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), new(71)).CarriedAsync(default)).Single().TriggerValue);
                await tx.CommitAsync(default);
            }
        });
        var subscriber = ActivatorUtilities.CreateInstance<MarriageTrialClearSubscriber>(f.Services);
        var invalid = new DungeonMapCleared(DungeonRunId.New(), 999, DungeonDifficulty.Normal, new(0, 0), 100, 1, DateTimeOffset.UtcNow,
            [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]) { IsBossRoom = true };
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                foreach (var rejected in new[]
                {
                    invalid,
                    invalid with { DungeonId = 2, IsBossRoom = false },
                    invalid with { DungeonId = 2, ClearedAt = DateTimeOffset.UnixEpoch },
                    invalid with { DungeonId = 2, Participants = [new(new(1), new(71), 0, true)] },
                    invalid with { DungeonId = 2, Participants = [new(new(1), new(71), 0, true), new(new(3), new(73), 1, false)] },
                })
                    await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), rejected), default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
            await using (var rollback = f.Scope.Begin())
            {
                var clear = invalid with { DungeonId = 2, ClearedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
                await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), clear.ClearedAt, Guid.NewGuid(), clear), default);
            }
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
            Assert.Equal(10, f.Scalar("SELECT SUM(trigger_value) FROM character_active_quests WHERE quest_id=20737"));
            for (var i = 0; i < 5; i++)
            {
                var clear = invalid with { RunId = DungeonRunId.New(), DungeonId = 2, ClearedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
                var envelope = new DomainEventEnvelope<DungeonMapCleared>(Guid.NewGuid(), Guid.NewGuid(), clear.ClearedAt, Guid.NewGuid(), clear);
                await using var tx = f.Scope.Begin();
                await subscriber.HandleAsync(envelope, default);
                await subscriber.HandleAsync(envelope, default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(10, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
        Assert.Equal(0, f.Scalar("SELECT SUM(trigger_value) FROM character_active_quests WHERE quest_id=20737"));
        Assert.True(await wedding.CanCompleteAsync(new(1), new(71), 20737, default));
        Assert.True(await wedding.CanCompleteAsync(new(2), new(72), 20737, default));
        await using var reopened = new LearningFixture(existing: f.Path, configure: s =>
        {
            s.AddSingleton<IQuestDefinitionSource, Definitions>();
            s.AddSingleton<IMarriageProposalContentSource, Content>();
        });
        Assert.True((await reopened.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(new(1), new(71), default)).IsEngaged);
        Assert.Equal(5, await reopened.Services.GetRequiredService<IMarriageQuestDomainStore>()
            .CountClearsAsync(new(72), 20737, (await reopened.Services.GetRequiredService<IMarriageQueryDomainService>().SnapshotAsync(new(2), new(72), default)).RelationId!.Value, default));
    }

    [Fact]
    public async Task Repeated_acceptance_preserves_progress_binding_and_clear_deduplication_after_reopen()
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        await Engage(f, peer); await Accept(f, 1, 71);
        var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
        var before = await bindings.ReadAsync(new(71), 20737, default);
        var clear = new DungeonMapCleared(DungeonRunId.New(), 2, DungeonDifficulty.Normal, new(0, 0), 100, 1,
            DateTimeOffset.UtcNow.AddSeconds(1), [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]) { IsBossRoom = true };
        var envelope = new DomainEventEnvelope<DungeonMapCleared>(Guid.NewGuid(), Guid.NewGuid(), clear.ClearedAt, Guid.NewGuid(), clear);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await ActivatorUtilities.CreateInstance<MarriageTrialClearSubscriber>(f.Services).HandleAsync(envelope, default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(4, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
        // Reopen the database so this also protects retries after server/session recovery.
        await using var reopened = new LearningFixture(existing: f.Path, configure: s =>
        {
            s.AddSingleton<IQuestDefinitionSource, Definitions>();
            s.AddSingleton<IQuestParameterSource, Parameters>();
            s.AddSingleton<IMarriageProposalContentSource, Content>();
            s.AddSingleton<IMarriageDungeonContentSource, Dungeons>();
            s.AddCommandHandler<AcceptQuestCommand, QuestAcceptOutcome, AcceptQuestCommandHandler>(CommandExecutionKind.Transactional);
        });
        var replay = await reopened.Services.GetRequiredService<ICommandBus>().SendAsync(new AcceptQuestCommand(new(1), new(71), 20737), default);
        Assert.False(replay.Accepted);
        Assert.Equal(QuestAcceptRefusal.AlreadyActive, replay.Refusal);
        Assert.Null(replay.QuestProgress);
        Assert.Empty(replay.Items);
        Assert.Equal(before, await reopened.Services.GetRequiredService<IMarriageQuestDomainStore>().ReadAsync(new(71), 20737, default));
        Assert.Equal(1, reopened.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
        await TestGameplay.RunAsync(reopened.Services, async () =>
        {
            await using (var tx = reopened.Scope.Begin())
            {
                await ActivatorUtilities.CreateInstance<MarriageTrialClearSubscriber>(reopened.Services).HandleAsync(envelope, default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(4, reopened.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("different-relation")]
    [InlineData("inactive-relation")]
    [InlineData("wrong-account")]
    public async Task Repeated_acceptance_cannot_repair_or_bypass_invalid_relationship_binding(string invalid)
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        Guid? oldRelation = null;
        if (invalid == "different-relation")
        {
            await MarriageProposalTests.Run(f, x => x.ProposeAsync(f.Session, peer, default));
            oldRelation = (await MarriageProposalTests.Run(f, x => x.RespondProposalAsync(peer, f.Session, 1, default))).Proposal!.Id;
        }
        await Engage(f, peer); await Accept(f, 1, 71);
        var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
        if (invalid == "missing") f.Sql("DELETE FROM marriage_quest_bindings WHERE character_id=71");
        if (invalid == "different-relation") f.Sql($"UPDATE marriage_quest_bindings SET proposal_id='{oldRelation:N}' WHERE character_id=71");
        if (invalid == "inactive-relation") f.Sql("UPDATE marriage_proposal_participants SET active=0");
        var before = await bindings.ReadAsync(new(71), 20737, default);
        var replay = await f.Services.GetRequiredService<ICommandBus>().SendAsync(
            new AcceptQuestCommand(new(invalid == "wrong-account" ? 2 : 1), new(71), 20737), default);
        Assert.False(replay.Accepted);
        Assert.Equal(before, await bindings.ReadAsync(new(71), 20737, default));
        Assert.Equal(5, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
    }

    [Fact]
    public async Task Give_up_then_accept_is_a_new_trial_with_a_fresh_clear_ledger()
    {
        var (f, peer) = await Fixture(); await using var owned = f;
        await Engage(f, peer); await Accept(f, 1, 71);
        var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
        var before = await bindings.ReadAsync(new(71), 20737, default);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await bindings.RecordClearAsync(new(71), 20737, before!.RelationId, DungeonRunId.New(), default));
                Assert.True(await f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), new(71)).GiveUpAsync(20737, default));
                await tx.CommitAsync(default);
            }
        });
        await Accept(f, 1, 71);
        Assert.Equal(5, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_trial_clears"));
        Assert.Equal(before!.RelationId, (await bindings.ReadAsync(new(71), 20737, default))!.RelationId);
    }

    [SkippableFact]
    public async Task Current_PVF_trial_hand_in_grants_box_then_material_hand_in_consumes_doll_once()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var (f, peer) = await Fixture(realContent: true); await using var owned = f;
        f.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,0),(72,0);");
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        foreach (var character in new[] { f.Session.CharacterId, peer.CharacterId })
            TestGameplay.Run(f.Services, () => containers.Register(character, new PersistentItemContainer(ContainerId.ForCharacter(character, InventoryContainerType.Main),
                new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(),
                f.Services.GetRequiredService<IDomainTransactionAccessor>(), f.Services.GetRequiredService<IItemPlacementRuleService>(),
                f.Services.GetRequiredService<IItemSlotRuleService>())));
        await Engage(f, peer); await Accept(f, 1, 71); await Accept(f, 2, 72);
        var subscriber = ActivatorUtilities.CreateInstance<MarriageTrialClearSubscriber>(f.Services);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var clear = new DungeonMapCleared(DungeonRunId.New(), 2, DungeonDifficulty.Normal, new(0, 0), 100, 1,
                    DateTimeOffset.UtcNow.AddSeconds(1), [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)]) { IsBossRoom = true };
                await using var tx = f.Scope.Begin();
                await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), clear.ClearedAt, Guid.NewGuid(), clear), default);
                await tx.CommitAsync(default);
            }
        });
        var settlement = f.Services.GetRequiredService<IQuestCompletionSettlementDomainService>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                Assert.True((await settlement.FinishAsync(new(1), new(71), 20737, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default, f.Session)).Finished);
                await tx.CommitAsync(default);
            }
        });
        var current = await f.Services.GetRequiredService<ICharacterInventoryEntryDomainService>().ReadCurrentAsync(new(1), new(71), default);
        Assert.Contains(current.Containers.SelectMany(x => x.Items), x => x.Core.ItemId == 10002616);
        Assert.Contains((await f.Services.GetRequiredService<IQuestQueryDomainService>().ReadAvailableAsync(new(1), new(71), default)).Quests, x => x.QuestId == 20738);
        Assert.DoesNotContain((await f.Services.GetRequiredService<IQuestQueryDomainService>().ReadAvailableAsync(new(2), new(72), default)).Quests, x => x.QuestId == 20738);
        Assert.True((await f.Services.GetRequiredService<ICommandBus>().SendAsync(new AcceptQuestCommand(new(1), new(71), 20738), default)).Accepted);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                Assert.False((await settlement.FinishAsync(new(1), new(71), 20738, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default, f.Session)).Finished);
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(new(1), new(71), [new(10002610, 1)],
                    "wedding-regression", default, ItemDeliveryMode.InventoryOnly);
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                Assert.True((await settlement.FinishAsync(new(1), new(71), 20738, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default, f.Session)).Finished);
                await tx.CommitAsync(default);
            }
            current = await f.Services.GetRequiredService<ICharacterInventoryEntryDomainService>().ReadCurrentAsync(new(1), new(71), default);
            Assert.DoesNotContain(current.Containers.SelectMany(x => x.Items), x => x.Core.ItemId == 10002610);
            Assert.Contains(current.Containers.SelectMany(x => x.Items), x => x.Core.ItemId == 10002901);
            await using (var tx = f.Scope.Begin())
            {
                Assert.False((await settlement.FinishAsync(new(1), new(71), 20738, ushort.MaxValue, 1, DateTimeOffset.UtcNow, default, f.Session)).Finished);
                await tx.CommitAsync(default);
            }
        });
    }
}
