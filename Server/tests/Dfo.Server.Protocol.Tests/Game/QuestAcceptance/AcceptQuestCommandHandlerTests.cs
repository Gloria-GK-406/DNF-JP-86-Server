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
        await using var fixture = await Fixture.CreateAsync();
        var handler = fixture.Handler(Quest(QuestId, trigger: 5));

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional, new AcceptQuestCommand(Account, Character, QuestId),
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
    }

    /// <remarks>
    /// The archive declares no item that can be held, so the container places nothing. A quest
    /// whose premise never arrived is not a quest that was accepted, and the row must not survive
    /// the use case that staged it.
    /// </remarks>
    [Fact]
    public async Task AQuestWhoseItemsCannotBePlacedIsHandedBackAndLeavesNoRow()
    {
        await using var fixture = await Fixture.CreateAsync(placementIsKnown: false);
        var handler = fixture.Handler(Quest(QuestId, items: [new QuestEventItem(8474, 1)]));

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional, new AcceptQuestCommand(Account, Character, QuestId),
            CancellationToken.None);
        await boundary.CommitAsync(CancellationToken.None);

        Assert.Equal(
            QuestAcceptOutcome.Refuse(QuestId, QuestAcceptRefusal.NoRoomForItems), outcome);
        Assert.Empty(await fixture.ReadQuestsAsync());
        Assert.Empty(await fixture.ReadItemsAsync());
    }

    /// <remarks>
    /// A quest the archive does not declare is refused before the log is asked anything, so no
    /// hand-back is involved and nothing is staged in the first place.
    /// </remarks>
    [Fact]
    public async Task AQuestTheArchiveDoesNotDeclareIsRefusedAndLeavesNoRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var handler = fixture.Handler(definition: null);

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional, new AcceptQuestCommand(Account, Character, QuestId),
            CancellationToken.None);
        await boundary.CommitAsync(CancellationToken.None);

        Assert.Equal(QuestAcceptOutcome.Refuse(QuestId, QuestAcceptRefusal.Unknown), outcome);
        Assert.Empty(await fixture.ReadQuestsAsync());
    }

    /// <summary>
    /// A registration that is not transactional is refused on the way in, not on the way to a write.
    /// </summary>
    /// <remarks>
    /// The refusals return before the log stages anything, so a check that lived at the write
    /// would let a mis-registered command answer three of its four outcomes normally and fail only
    /// on the fourth.
    /// </remarks>
    [Fact]
    public async Task AnExecutionThatIsNotTransactionalIsRefusedBeforeAnythingIsAsked()
    {
        await using var fixture = await Fixture.CreateAsync();
        var handler = fixture.Handler(definition: null);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.HandleAsync(
                Transactional with { ExecutionKind = CommandExecutionKind.Direct },
                new AcceptQuestCommand(Account, Character, QuestId),
                CancellationToken.None));

        Assert.Contains("registered transactional", refusal.Message, StringComparison.Ordinal);
    }

    private static CommandExecutionContext Transactional => new(
        Guid.Empty, typeof(AcceptQuestCommand), typeof(AcceptQuestCommandHandler),
        CommandExecutionKind.Transactional);

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

        public DomainTransactionScope Scope { get; } = new();

        public static async Task<Fixture> CreateAsync(bool placementIsKnown = true)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-questaccept-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(path, placementIsKnown);
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'accept')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        public AcceptQuestCommandHandler Handler(QuestDefinition? definition)
        {
            var containers = new CharacterContainerDomainService();
            containers.Register(Character, new PersistentItemContainer(
                ContainerId.ForCharacter(Character, InventoryContainerType.Main),
                Character,
                new ContainerShape(InventoryContainerType.Main, 0),
                new SqliteItemDomainStore(this.Path),
                this.Scope,
                null!,
                new ItemPlacementRuleService(
                    new Placements(this.PlacementIsKnown), new NoWearables()),
                new ItemArrivalRuleService(),
                new ItemSlotRuleService()));
            return new AcceptQuestCommandHandler(
                new QuestDefinitionDomainService(
                    new QuestContentRuleService(new Quests(definition), new NoQuestParameters())),
                new CharacterQuestLogDomainService(
                    new SqliteQuestDomainStore(this.Path), new AmbientStoreAccess(this.Scope)),
                containers);
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
                "SELECT slot_index FROM items WHERE owner_scope='character' AND owner_id=60";
            var rows = new List<int>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetInt32(0));
            }

            return rows;
        }

        private static SqliteConnection Open(string path)
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
        public ValueTask<byte?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte?>(null);
    }

    /// <summary>Where items go, or that the archive declares no such item at all.</summary>
    private sealed class Placements(bool known) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(known ? new ItemPlacement(2, 100, 65, 120) : null);
    }
}
