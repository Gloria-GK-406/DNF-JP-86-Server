using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestClearCouponDomainServiceTests
{
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(6), new CharacterId(60), new NetworkConnectionId("coupon"), 1, 10011);
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public async Task SelectedQuestsAreClearedBeforeOneCouponIsConsumedWithoutRewards()
    {
        var events = new List<string>();
        var inventory = new RecordingInventory(events);
        var log = new RecordingLog(events) { Completed = [101, 102] };
        var rules = new RecordingRule(events) { Selected = [101, 102] };
        var progress = new RecordingProgress(events);
        var service = Service(inventory, log, rules, progress);

        var result = await service.UseAsync(Session, 69, Now, CancellationToken.None);

        Assert.Equal([101, 102], result.CompletedQuestIds);
        Assert.Same(inventory.Receipt, result.Inventory);
        Assert.Equal(["source", "cleared", "rule", "complete", "report", "settle"], events);
        Assert.Equal([101, 102], progress.Facts!.Select(fact => Assert.IsType<QuestCleared>(fact).QuestId));
        Assert.Equal([new OpeningSlotConsumption(69, 10_006_007, 1)], inventory.Consumed);
        Assert.True(inventory.Grants.IsEmpty);
        Assert.Equal((byte)60, rules.Level);
        Assert.Equal((byte)0, rules.Job);
        Assert.Equal((byte)0, rules.GrowType);
    }

    [Fact]
    public async Task NoEligibleQuestDoesNotChangeLogOrConsumeCoupon()
    {
        var events = new List<string>();
        var inventory = new RecordingInventory(events);
        var log = new RecordingLog(events);
        var rules = new RecordingRule(events) { Selected = [] };

        var rejection = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await Service(inventory, log, rules).UseAsync(Session, 69, Now, CancellationToken.None));

        Assert.Equal(OpeningRejection.InvalidChoice, rejection.Reason);
        Assert.Equal(["source", "cleared", "rule"], events);
    }

    [Fact]
    public async Task ChangedClearStateDoesNotConsumeCoupon()
    {
        var events = new List<string>();
        var inventory = new RecordingInventory(events);
        var log = new RecordingLog(events) { Completed = [] };
        var rules = new RecordingRule(events) { Selected = [101] };

        var rejection = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await Service(inventory, log, rules).UseAsync(Session, 69, Now, CancellationToken.None));

        Assert.Equal(OpeningRejection.InvalidChoice, rejection.Reason);
        Assert.Equal(["source", "cleared", "rule", "complete"], events);
    }

    [Fact]
    public async Task AnotherItemDoesNotReachQuestRuleOrSettlement()
    {
        var events = new List<string>();
        var inventory = new RecordingInventory(events) { ItemId = 123 };
        var log = new RecordingLog(events);
        var rules = new RecordingRule(events) { Selected = [101] };

        var rejection = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await Service(inventory, log, rules).UseAsync(Session, 69, Now, CancellationToken.None));

        Assert.Equal(OpeningRejection.UnsupportedContent, rejection.Reason);
        Assert.Equal(["source"], events);
    }

    private static QuestClearCouponDomainService Service(
        RecordingInventory inventory, RecordingLog log, RecordingRule rules,
        RecordingProgress? progress = null) =>
        new(inventory, CurrentCharacterStub.Domain(), new OneLog(log), rules,
            progress ?? new RecordingProgress([]));

    private sealed class RecordingProgress(List<string> events) : IQuestProgressDomainService
    {
        public IReadOnlyList<QuestProgressFact>? Facts { get; private set; }

        public ValueTask<QuestProgressReport> ReportAsync(AccountId accountId,
            CharacterId characterId, IReadOnlyList<QuestProgressFact> facts,
            CancellationToken cancellationToken)
        {
            events.Add("report");
            Assert.Equal(Session.AccountId, accountId);
            Assert.Equal(Session.CharacterId, characterId);
            this.Facts = facts;
            return ValueTask.FromResult(new QuestProgressReport(null, [], null));
        }
    }

    private sealed class RecordingInventory(List<string> events) : IOpeningInventoryDomainService
    {
        public int ItemId { get; init; } = 10_006_007;
        public IReadOnlyList<OpeningSlotConsumption>? Consumed { get; private set; }
        public ImmutableArray<OpeningPreparedGrant> Grants { get; private set; }
        public OpeningInventoryReceipt Receipt { get; } = new(Session,
            ImmutableArray<CommittedInventoryUpdateState>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<OpeningRewardDisplay>.Empty, Now);

        public ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session,
            int slot, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Assert.Equal(Session, session);
            Assert.Equal(69, slot);
            Assert.Equal(Now, now);
            events.Add("source");
            return ValueTask.FromResult(new ContainerSlotItem(slot,
                StackableItemCore.Create(2, ItemId, 2), ItemWorldClass.Interchangeable));
        }

        public ValueTask<OpeningInventoryReceipt> SettleAsync(SelectedSessionIdentity session,
            IReadOnlyList<OpeningSlotConsumption> consumed,
            ImmutableArray<OpeningPreparedGrant> grants, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            events.Add("settle");
            this.Consumed = consumed;
            this.Grants = grants;
            return ValueTask.FromResult(this.Receipt);
        }
    }

    private sealed class RecordingRule(List<string> events) : IQuestClearCouponRuleService
    {
        public IReadOnlyList<int> Selected { get; init; } = [];
        public byte Level { get; private set; }
        public byte Job { get; private set; }
        public byte GrowType { get; private set; }

        public ValueTask<IReadOnlyList<int>> SelectAsync(int itemId, byte level, byte job,
            byte growType, IReadOnlyList<ClearedQuest> cleared, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            events.Add("rule");
            Assert.Equal(10_006_007, itemId);
            Assert.Empty(cleared);
            this.Level = level;
            this.Job = job;
            this.GrowType = growType;
            return ValueTask.FromResult(this.Selected);
        }
    }

    private sealed class OneLog(RecordingLog log) : ICharacterQuestLogDomainService
    {
        public IQuestLog LogOf(AccountId accountId, CharacterId characterId)
        {
            Assert.Equal(Session.AccountId, accountId);
            Assert.Equal(Session.CharacterId, characterId);
            return log;
        }
    }

    private sealed class RecordingLog(List<string> events) : IQuestLog
    {
        public IReadOnlyList<int> Completed { get; init; } = [];
        public CharacterId CharacterId => Session.CharacterId;

        public ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(CancellationToken cancellationToken)
        {
            events.Add("cleared");
            return ValueTask.FromResult<IReadOnlyList<ClearedQuest>>([]);
        }

        public ValueTask<IReadOnlyList<int>> CompleteWithoutRewardsAsync(
            IReadOnlyCollection<int> questIds, CancellationToken cancellationToken)
        {
            events.Add("complete");
            return ValueTask.FromResult(this.Completed);
        }

        public ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CarriedQuest?> FindCarriedAsync(int questId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<QuestAcceptance> AcceptAsync(QuestDefinition definition, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> RecordProgressAsync(CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<QuestAdvance> AdvanceAsync(QuestDefinition definition, byte selector, bool increment, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> CompleteAsync(int questId, byte mark, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<QuestFinishVerdict> FinishAsync(int questId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask TrackAsync(IReadOnlyList<int> questIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
