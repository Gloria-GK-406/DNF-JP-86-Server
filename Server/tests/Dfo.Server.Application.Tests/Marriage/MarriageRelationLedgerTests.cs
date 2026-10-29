using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageRelationLedgerTests
{
    private static readonly Dictionary<(MarriageStage?, MarriageTrigger), MarriageStage> Table = new()
    {
        [(null, MarriageTrigger.Propose)] = MarriageStage.Proposed,
        [(MarriageStage.Proposed, MarriageTrigger.AcceptProposal)] = MarriageStage.Engaged,
        [(MarriageStage.Proposed, MarriageTrigger.RefuseProposal)] = MarriageStage.Closed,
        [(MarriageStage.Proposed, MarriageTrigger.ProposalLapsed)] = MarriageStage.Closed,
        [(MarriageStage.Engaged, MarriageTrigger.RequestCeremony)] = MarriageStage.Requested,
        [(MarriageStage.Requested, MarriageTrigger.AcceptCeremony)] = MarriageStage.Preparing,
        [(MarriageStage.Requested, MarriageTrigger.CeremonyWithdrawn)] = MarriageStage.Engaged,
        [(MarriageStage.Preparing, MarriageTrigger.WeddingCompleted)] = MarriageStage.Married,
        [(MarriageStage.Preparing, MarriageTrigger.WeddingCancelled)] = MarriageStage.Engaged,
    };

    private static CoupleLedger Ledger(LearningFixture f) => f.Services.GetRequiredService<CoupleLedger>();
    private static async Task<MarriageRelation> RelationAsync(LearningFixture f, Guid id) => (await Ledger(f).ReadAsync(id, default))!;
    private static int WeddingRows(LearningFixture f, Guid relation, int phase) =>
        f.Scalar($"SELECT COUNT(*) FROM marriage_weddings WHERE relation_id='{relation:N}' AND phase={phase}");

    [Fact]
    public void Every_stage_and_trigger_pair_follows_the_transition_table_or_is_refused()
    {
        var stages = new MarriageStage?[] { null }.Concat(Enum.GetValues<MarriageStage>().Select(x => (MarriageStage?)x)).ToArray();
        var triggers = Enum.GetValues<MarriageTrigger>();
        Assert.Equal(7, stages.Length); Assert.Equal(9, triggers.Length);
        foreach (var from in stages)
            foreach (var trigger in triggers)
                Assert.Equal(Table.TryGetValue((from, trigger), out var to) ? to : null, MarriageStageRules.Next(from, trigger));
    }

    [Fact]
    public async Task A_second_commit_from_the_same_revision_is_refused_and_leaves_the_stage()
    {
        await using var w = await ChurchTownAsync(); var f = w.F;
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, w.Peer, default))).Succeeded);
        var read = (await Ledger(f).FindAsync(f.Session.CharacterId, default))!;
        Assert.True(await InTx(f, () => Ledger(f).CommitAsync(read, MarriageTrigger.AcceptProposal, null, null, default)));
        Assert.False(await InTx(f, () => Ledger(f).CommitAsync(read, MarriageTrigger.RefuseProposal, null, null, default)));
        var engaged = await RelationAsync(f, read.Id);
        Assert.Equal(MarriageStage.Engaged, engaged.Stage); Assert.Equal(read.Revision + 1, engaged.Revision);
        Assert.False(await InTx(f, () => Ledger(f).CommitAsync(engaged, MarriageTrigger.WeddingCompleted, null, null, default)));
        Assert.Equal(engaged, await RelationAsync(f, read.Id));
    }

    [Fact]
    public async Task Proposal_answers_and_expiry_move_the_relation_through_proposed_engaged_and_closed()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var third = AddCharacter(f, 3, 73, job: 0);
        async Task<Guid> Propose(SelectedSessionIdentity to) =>
            (await InTx(f, () => Couple(f).ProposeAsync(f.Session, to, default))).Proposal!.Id;

        var refused = await Propose(peer);
        var proposed = await RelationAsync(f, refused);
        Assert.Equal(MarriageStage.Proposed, proposed.Stage); Assert.Null(proposed.CeremonyId);
        await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 1, default));
        Assert.Equal(MarriageStage.Closed, (await RelationAsync(f, refused)).Stage);

        var timedOut = await Propose(peer);
        w.Clock.Now += TimeSpan.FromHours(1);
        Assert.True((await InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 2, default))).Succeeded);
        Assert.Equal(MarriageStage.Closed, (await RelationAsync(f, timedOut)).Stage);

        var lapsed = await Propose(peer);
        w.Clock.Now += TimeSpan.FromHours(1);
        var replacement = await Propose(third);
        Assert.Equal(MarriageStage.Closed, (await RelationAsync(f, lapsed)).Stage);
        Assert.Equal(MarriageStage.Proposed, (await RelationAsync(f, replacement)).Stage);
        Assert.Equal(replacement, (await Ledger(f).FindAsync(f.Session.CharacterId, default))!.Id);

        await InTx(f, () => Couple(f).RespondProposalAsync(third, f.Session, 0, default));
        var engaged = await RelationAsync(f, replacement);
        Assert.Equal(MarriageStage.Engaged, engaged.Stage); Assert.Null(engaged.CeremonyId);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_relations WHERE stage NOT IN (0,5)"));
    }

    [Fact]
    public async Task Ceremony_and_wedding_transitions_carry_the_reservation_and_keep_one_wedding_per_stage()
    {
        await using var w = await ChurchTownAsync(Tier0()); var f = w.F; var peer = w.Peer;
        var id = await EligibleAsync(w, f.Session, peer);
        async Task<Guid> Request() => (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Ceremony!.Id;
        async Task Expect(MarriageStage stage, Guid? ceremony)
        {
            var relation = await RelationAsync(f, id);
            Assert.Equal(stage, relation.Stage); Assert.Equal(ceremony, relation.CeremonyId);
        }
        await Expect(MarriageStage.Engaged, null);

        var refused = await Request();
        await Expect(MarriageStage.Requested, refused);
        await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 1, default));
        await Expect(MarriageStage.Engaged, null);

        await Request();
        w.Clock.Now += TimeSpan.FromHours(1);
        Assert.True((await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 2, default))).Succeeded);
        await Expect(MarriageStage.Engaged, null);

        var bus = DomainCommandFixture.Bus(f.Services);
        var cancelled = await Request();
        var accepted = await bus.SendAsync(new CeremonyTestCommand(peer, f.Session, 0, 0, 0));
        Assert.Equal(2, accepted.Admissions.Count);
        await Expect(MarriageStage.Preparing, cancelled);
        Assert.Equal(1, WeddingRows(f, id, 0));
        w.Clock.Now = accepted.Admissions[0].Church.Room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Cancelled, (await InTx(f, () => Venue(f).AdvanceAsync(cancelled, default)))!.Wedding.Phase);
        await Expect(MarriageStage.Engaged, null);
        Assert.Equal(0, WeddingRows(f, id, 0) + WeddingRows(f, id, 1));

        f.Sql("UPDATE character_stats SET carried_gold=500000 WHERE character_id=71");
        var completed = await Request();
        var second = await bus.SendAsync(new CeremonyTestCommand(peer, f.Session, 0, 0, 0));
        await Expect(MarriageStage.Preparing, completed);
        Assert.Equal(1, WeddingRows(f, id, 0));
        var room = second.Admissions[0].Church.Room;
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        Assert.Equal(MarriageWeddingPhase.Completed, (await InTx(f, () => Venue(f).AdvanceAsync(completed, default)))!.Wedding.Phase);
        await Expect(MarriageStage.Married, completed);
        Assert.Equal(room.ReadyUntil.ToUnixTimeMilliseconds(), (await RelationAsync(f, id)).MarriedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(0, WeddingRows(f, id, 0)); Assert.Equal(1, WeddingRows(f, id, 1));
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM marriage_ceremonies WHERE relation_id='{id:N}' AND state=1"));
    }

    [Fact]
    public async Task A_relation_out_of_step_with_its_ceremony_is_stale_before_the_ceremony_changes()
    {
        await using var w = await ChurchTownAsync(Tier0()); var f = w.F; var peer = w.Peer;
        var id = await EligibleAsync(w, f.Session, peer);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        f.Sql($"UPDATE marriage_relations SET stage=1,ceremony_id=NULL,revision=revision+1 WHERE id='{id:N}'");
        Assert.Equal(MarriageCeremonyFailure.Stale, (await InTx(f, () => Couple(f).RespondReservationAsync(peer, f.Session, 0, default))).Failure);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_ceremonies"));
        Assert.Equal(MarriageStage.Engaged, (await RelationAsync(f, id)).Stage);
    }

    [Fact]
    public async Task Using_a_love_coupon_updates_only_the_actor_personal_wallet()
    {
        await using var w = await ChurchTownAsync(configure: s =>
        {
            s.AddSingleton<IMarriageLovePointRuleService, LoveRules>();
            s.AddSingleton<IItemPlacementDefinitionSource, LoveRules>();
        });
        var f = w.F; var peer = w.Peer;
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        TestGameplay.Run(f.Services, () => containers.Register(f.Session.CharacterId, new PersistentItemContainer(
            ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
            f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        await InTx(f, async () =>
        {
            await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(f.Session.AccountId, f.Session.CharacterId,
                [new(LoveRules.Coupon, 2)], "love-test", default, ItemDeliveryMode.InventoryOnly);
            return 0;
        });
        var stored = (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default)).Single();
        var coupon = new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, stored.StorageSlotIndex));
        var (ceremony, room) = await PreparedAsync(w);
        Arrive(f, f.Session, room); Arrive(f, peer, room);
        w.Clock.Now = room.ReadyUntil;
        await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));

        var used = await InTx(f, () => TestEffectFootprints.Run(f.Session, cap => Couple(f).UseLovePointAsync(f.Session, coupon, [f.Session.AccountId, peer.AccountId], cap, default)));
        Assert.Equal(0, used.Status); Assert.Equal(1, used.Points);
        Assert.Equal(1, await Ledger(f).PointsAsync(ceremony.RelationId, f.Session.CharacterId, default));
        Assert.Equal(0, (await Query(f).SnapshotAsync(peer.AccountId, peer.CharacterId, default)).LovePoints);
    }

    [Fact]
    public async Task Startup_audit_warns_only_when_an_engaged_relation_still_holds_a_reserved_ceremony()
    {
        await using var w = await ChurchTownAsync(Tier0()); var f = w.F; var peer = w.Peer;
        var id = await EligibleAsync(w, f.Session, peer);
        var logger = new RecordingLogger();
        var audit = new MarriageRelationAudit(Ledger(f), logger);
        await audit.StartAsync(default);
        Assert.Empty(logger.Warnings);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Succeeded);
        f.Sql($"UPDATE marriage_ceremonies SET state=1; UPDATE marriage_relations SET stage=1,ceremony_id=NULL WHERE id='{id:N}'");
        await audit.StartAsync(default);
        Assert.Contains("1 engaged", Assert.Single(logger.Warnings));
    }

    private sealed class LoveRules : IMarriageLovePointRuleService, IItemPlacementDefinitionSource
    {
        public const int Coupon = 10002901;
        public ValueTask<MarriageLovePointItemRule?> RuleForAsync(int item, CancellationToken ct) => ValueTask.FromResult<MarriageLovePointItemRule?>(item == Coupon
            ? new(new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, Action: "[love point coupon]"), 1, 999) : null);
        public ValueTask<ItemPlacement?> GetAsync(int item, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private sealed class RecordingLogger : ILogger<MarriageRelationAudit>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
