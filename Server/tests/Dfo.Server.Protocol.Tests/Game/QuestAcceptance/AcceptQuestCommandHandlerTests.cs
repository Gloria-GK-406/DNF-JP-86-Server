using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.QuestAcceptance;

/// <summary>
/// What the handler does once the log has answered.
/// </summary>
/// <remarks>
/// The judgement itself is the log's and is proven there. What is left here is the ordering the
/// handler owns, and one branch of it exists only in the handler: a quest that was taken on and
/// then could not be paid for. The quest is staged before the items are placed, so the only thing
/// standing between a refusal and a stored quest with nothing behind it is the handler handing the
/// quest back -- and nothing else in the repository exercises that.
///
/// The store and the container are the production ones over a real database, because a fake log
/// would answer the very question being asked: whether what was staged is really gone.
/// </remarks>
public sealed class AcceptQuestCommandHandlerTests
{
    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    private const ushort QuestId = 1016;

    [Fact]
    public async Task AQuestTakenOnLandsInTheLogAndTheAnswerNamesTheArchivesProgress()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var handler = fixture.Handler(Quest(QuestId, trigger: 5));

            await using var boundary = fixture.Scope.Begin();
            var outcome = await handler.HandleAsync(
                Transactional, new QuestAcceptOperation(Account, Character, QuestId),
                CancellationToken.None);
            await boundary.CommitAsync(CancellationToken.None);

            // Field by field: the record's Items is compared by reference, so an equal-looking
            // outcome built here would fail against the list the handler returns.
            Assert.True(outcome.Accepted);
            Assert.Equal(QuestId, outcome.QuestId);
            Assert.Equal(5u, outcome.InitialTrigger);
            Assert.Empty(outcome.Items);
            Assert.Null(outcome.Refusal);
            Assert.Equal([(0, QuestId, 5)], await fixture.ReadQuestsAsync());
        });
    }

    [Fact]
    public async Task Already_cleared_dependency_keeps_archive_CMD_value_but_projects_current_progress()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            await using (var connection = Fixture.Open(fixture.Path))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) "
                    + "VALUES(60,1016,1)";
                await command.ExecuteNonQueryAsync();
            }

            var definition = Quest(2002, trigger: 1) with
            {
                ObjectiveTypeCode = "clear quest",
                ClearedQuestRequirements = [1016],
            };
            var handler = fixture.Handler(definition);
            await using var boundary = fixture.Scope.Begin();
            var outcome = await handler.HandleAsync(
                Transactional, new QuestAcceptOperation(Account, Character, 2002),
                CancellationToken.None);
            await boundary.CommitAsync(CancellationToken.None);

            Assert.True(outcome.Accepted);
            Assert.Equal(1u, outcome.InitialTrigger);
            Assert.Equal(0, Assert.Single(outcome.QuestProgress!.Quests).TriggerValue);
            Assert.Equal([(0, 2002, 0)], await fixture.ReadQuestsAsync());
        });
    }

    [Fact]
    public async Task Acceptance_items_report_holdings_change_and_project_result_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            fixture.Progress.Snapshot = new QuestProgressSnapshot([new CarriedQuest(0, QuestId, 0)]);
            var handler = fixture.Handler(Quest(QuestId, items: [new QuestEventItem(8474, 1)]));

            await using var boundary = fixture.Scope.Begin();
            var outcome = await handler.HandleAsync(
                Transactional, new QuestAcceptOperation(Account, Character, QuestId),
                CancellationToken.None);
            await boundary.CommitAsync(CancellationToken.None);

            Assert.True(outcome.Accepted);
            Assert.Contains(fixture.Progress.Facts, fact => fact is QuestHoldingsChanged);
            Assert.Same(fixture.Progress.Snapshot, outcome.QuestProgress);
        });
    }

    /// <remarks>
    /// The archive declares no item that can be held, so the container places nothing. A quest
    /// whose premise never arrived is not a quest that was accepted, and the row must not survive
    /// the use case that staged it.
    /// </remarks>
    [Fact]
    public async Task Unknown_quest_reward_aborts_acceptance_and_leaves_no_row()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync(placementIsKnown: false);
            var handler = fixture.Handler(Quest(QuestId, items: [new QuestEventItem(8474, 1)]));

            await using var boundary = fixture.Scope.Begin();
            await Assert.ThrowsAsync<InvalidDataException>(async () => await handler.HandleAsync(
                Transactional, new QuestAcceptOperation(Account, Character, QuestId), CancellationToken.None));
            await boundary.DisposeAsync();
            Assert.Empty(await fixture.ReadQuestsAsync());
            Assert.Empty(await fixture.ReadItemsAsync());
        });
    }

    /// <remarks>
    /// A quest the archive does not declare is refused before the log is asked anything, so no
    /// hand-back is involved and nothing is staged in the first place.
    /// </remarks>
    [Fact]
    public async Task AQuestTheArchiveDoesNotDeclareIsRefusedAndLeavesNoRow()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var handler = fixture.Handler(definition: null);

            await using var boundary = fixture.Scope.Begin();
            var outcome = await handler.HandleAsync(
                Transactional, new QuestAcceptOperation(Account, Character, QuestId),
                CancellationToken.None);
            await boundary.CommitAsync(CancellationToken.None);

            Assert.Equal(QuestAcceptOutcome.Refuse(QuestId, QuestAcceptRefusal.Unknown), outcome);
            Assert.Empty(await fixture.ReadQuestsAsync());
        });
    }

    /// <summary>The actual Protocol route owns the explicit write; no legacy fine registration remains.</summary>
    /// <remarks>The wire oracle separately proves an ordinary empty-participant write for every acceptance.</remarks>
    [Fact]
    public void Actual_route_is_Direct_Protocol_and_has_no_legacy_fine_registration()
    {
        var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new ProtocolModule().ConfigureServices(services,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var registrations=Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,new object());
        var rows=services.Where(x=>x.ServiceType==typeof(CommandHandlerRegistration)).Select(x=>(CommandHandlerRegistration)x.ImplementationInstance!).ToArray();
        var whole=rows.SingleOrDefault(x=>x.CommandType.Name=="GameAcceptQuestCommand");

        Assert.NotNull(whole);Assert.Equal(CommandRouteKind.Protocol,whole.Route);
        Assert.Single(whole.ProtocolMessages!);Assert.DoesNotContain(rows,x=>x.CommandType.Name=="AcceptQuestCommand");
    }

    [Fact]
    public async Task Repeated_acceptance_answers_0012_without_changing_progress_items_or_acceptance_time()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var handler = fixture.Handler(Quest(QuestId, trigger: 5, items: [new QuestEventItem(8474, 1)]));
            await using (var tx = fixture.Scope.Begin())
            {
                Assert.True((await handler.HandleAsync(Transactional, new(Account, Character, QuestId), default)).Accepted);
                await tx.CommitAsync(default);
            }
            await using (var connection = Fixture.Open(fixture.Path))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE character_active_quests SET trigger_value=4 WHERE character_id=60; "
                    + "UPDATE character_quest_acceptance_times SET accepted_at_ticks=123 WHERE character_id=60";
                await command.ExecuteNonQueryAsync();
            }

            await using (var tx = fixture.Scope.Begin())
            {
                var replay = await handler.HandleAsync(Transactional, new(Account, Character, QuestId), default);
                Assert.False(replay.Accepted);
                Assert.Equal(QuestAcceptRefusal.AlreadyActive, replay.Refusal);
                Assert.Empty(replay.Items);
                Assert.Null(replay.QuestProgress);
                Assert.Equal(new byte[] { 0x00, 0x12 }, AcceptQuestPayloadCodec.Encode(
                    new(replay.Accepted, replay.QuestId, replay.InitialTrigger, replay.Items, (byte)replay.Refusal.GetValueOrDefault())));
                await tx.CommitAsync(default);
            }
            Assert.Equal([(0, QuestId, 4)], await fixture.ReadQuestsAsync());
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT SUM(count) FROM item_slots WHERE owner_scope='character' AND owner_id=60"));
            Assert.Equal(123L, await fixture.ScalarAsync("SELECT accepted_at_ticks FROM character_quest_acceptance_times WHERE character_id=60"));
        });
    }

    [Fact]
    public async Task Given_up_quest_can_be_accepted_again()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var handler = fixture.Handler(Quest(QuestId, trigger: 5));
            await using (var tx = fixture.Scope.Begin())
            {
                Assert.True((await handler.HandleAsync(Transactional, new(Account, Character, QuestId), default)).Accepted);
                await tx.CommitAsync(default);
            }
            await using (var tx = fixture.Scope.Begin())
            {
                var logs = new CharacterQuestLogDomainService(
                    new SqliteQuestDomainStore(fixture.Path), new AmbientStoreAccess(fixture.Scope), clock: TimeProvider.System);
                Assert.True(await logs.LogOf(Account, Character).GiveUpAsync(QuestId, default));
                await tx.CommitAsync(default);
            }
            await using (var tx = fixture.Scope.Begin())
            {
                var accepted = await handler.HandleAsync(Transactional, new(Account, Character, QuestId), default);
                Assert.True(accepted.Accepted);
                Assert.Equal(5u, accepted.InitialTrigger);
                await tx.CommitAsync(default);
            }
            Assert.Equal([(0, QuestId, 5)], await fixture.ReadQuestsAsync());
        });
    }

    [Fact]
    public async Task Cleared_nonrepeatable_quest_is_still_refused()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            await using (var connection = Fixture.Open(fixture.Path))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(60,1016,1)";
                await command.ExecuteNonQueryAsync();
            }
            await using var tx = fixture.Scope.Begin();
            var outcome = await fixture.Handler(Quest(QuestId)).HandleAsync(Transactional, new(Account, Character, QuestId), default);
            Assert.False(outcome.Accepted);
            Assert.Equal(QuestAcceptRefusal.AlreadyActive, outcome.Refusal);
            await tx.CommitAsync(default);
            Assert.Empty(await fixture.ReadQuestsAsync());
        });
    }

    private static CommandExecutionContext Transactional => new(
        Guid.Empty, typeof(QuestAcceptOperation), typeof(QuestAcceptOperationDriver));

    private static QuestDefinition Quest(
        int questId, uint trigger = 1, IReadOnlyList<QuestEventItem>? items = null) =>
        new(questId, "", null, null, null, null, null, null, [], "", "",
            null, null, null, null, null, null, null, [], [], [], [],
            InitialTrigger: trigger, EventItems: items);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path, bool placementIsKnown)
        {
            this.Path = path;
            this.PlacementIsKnown = placementIsKnown;
        }

        public string Path { get; }

        public bool PlacementIsKnown { get; }

        public DomainTransactionScope Scope { get; } = new(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Transactions.DomainTransaction>.Instance, outboxes: TestRequired.Outboxes());

        public RecordingQuestProgress Progress { get; } = new();

        public static async Task<Fixture> CreateAsync(bool placementIsKnown = true)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-questaccept-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, TestRequired.TransactionScope());
            var fixture = new Fixture(path, placementIsKnown);
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'accept')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        public QuestAcceptOperationDriver Handler(QuestDefinition? definition)
        {
            var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
            containers.Register(Character, new PersistentItemContainer(
                ContainerId.ForCharacter(Character, InventoryContainerType.Main),
                new ContainerShape(InventoryContainerType.Main, 0),
                new SqliteItemDomainStore(this.Path, TestRequired.NoContentsEvents),
                this.Scope,
                new ItemPlacementRuleService(
                    new Placements(this.PlacementIsKnown), new NoWearables()),
                new ItemSlotRuleService()));
            return new QuestAcceptOperationDriver(
                new QuestDefinitionDomainService(
                    new QuestContentRuleService(new Quests(definition), new NoQuestParameters()),
                    new QuestRewardRuleService(cycles: TestRequired.Absent<Dfo.Server.Abstractions.World.ICycleDungeonRuleService>())),
                new CharacterQuestLogDomainService(
                    new SqliteQuestDomainStore(this.Path), new AmbientStoreAccess(this.Scope), clock: TimeProvider.System),
                containers,
                this.Progress,
                new Dfo.Server.Persistence.Tests.Support.BagRewardDelivery(new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(
                    new ItemPlacementRuleService(new Placements(this.PlacementIsKnown), new NoWearables()),
                    new ItemLifecycleDomainService(new SqliteItemDomainStore(this.Path, TestRequired.NoContentsEvents), this.Scope,
                        new ItemPlacementRuleService(new Placements(this.PlacementIsKnown), new NoWearables()), clock: TimeProvider.System, expiration: null!)), containers));
        }

        public async Task<List<(int Slot, int QuestId, int TriggerValue)>> ReadQuestsAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot,quest_id,trigger_value FROM character_active_quests "
                + "WHERE character_id=60 ORDER BY slot";
            var rows = new List<(int, int, int)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
            }

            return rows;
        }

        public async Task<List<int>> ReadItemsAsync()
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT slot_index FROM item_slots "
                + "WHERE owner_scope='character' AND owner_id=60";
            var rows = new List<int>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetInt32(0));
            }

            return rows;
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingQuestProgress : IQuestProgressDomainService
    {
        public List<QuestProgressFact> Facts { get; } = [];

        public QuestProgressSnapshot? Snapshot { get; set; }

        public ValueTask<QuestProgressReport> ReportAsync(
            AccountId accountId, CharacterId characterId,
            IReadOnlyList<QuestProgressFact> facts,
            CancellationToken cancellationToken)
        {
            Facts.AddRange(facts);
            return ValueTask.FromResult(new QuestProgressReport(null, [], this.Snapshot));
        }
    }

    /// <summary>Nothing reached through here works out a quest reward.</summary>
    private sealed class NoQuestParameters : IQuestParameterSource
    {
        public QuestParameters Load() =>
            throw new NotSupportedException(
                "No test on this path asks what a quest pays; a value here would be invented.");
    }

    private sealed class Quests(QuestDefinition? definition) : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>(
                definition is null ? [] : [definition]);

        public ValueTask<QuestDefinition?> GetAsync(
            int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(definition?.QuestId == questId ? definition : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }

    /// <summary>Nothing here is worn; these tests never put a piece on.</summary>
    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    /// <summary>Where items go, or that the archive declares no such item at all.</summary>
    private sealed class Placements(bool known) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(known ? new ItemPlacement(2, 100,
                new(BusinessInventorySpace.Consumable, 0, 55)) : null);
    }
}
