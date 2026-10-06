using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Protocol.Game.MoveItemSpace;

namespace Dfo.Server.Application.Tests.Game;

/// <summary>
/// A contract creature taken off the character can be put back on.
/// </summary>
/// <remarks>
/// This is the one behaviour this outcome was allowed to change, and it was asymmetric rather than
/// simply absent. Taking a creature off asked the bag whether the slot it was aimed at would have
/// it, and the bag said yes; putting one on asked the archive which slot it is worn in, and the
/// table of slot names had no entry for <c>[creature]</c>, so the answer was that it is not
/// wearable at all. A player who dragged their creature into the bag could not put it back.
/// <para>
/// It runs against the operator's own archive and a real database, because both halves of the
/// thing being asserted live outside the code under test: which slot the creature's own definition
/// resolves to is the archive's answer, and whether the creature that comes back is the same
/// creature is a question only an identity that outlives the position can answer. Stubbing either
/// would leave the test green with the fix removed.
/// </para>
/// </remarks>
public sealed class CreatureGoesBackOnTests
{
    /// <summary>
    /// <c>equipment/creature/faras.equ</c>, whose <c>[equipment type]</c> is <c>[creature]</c>.
    /// </summary>
    private const int ContractCreatureItemId = 63000;

    /// <summary>What a creature's item core says it is.</summary>
    private const byte CreatureKind = 5;

    /// <summary>The slot this server keeps a worn creature in. Not this outcome's to decide.</summary>
    private const short CreatureSlot = 25;

    /// <summary>The creature inventory's first local slot.</summary>
    private const short BagSlot = 0;

    private const int Character = 77;

    private const byte PetList = (byte)InventoryContainerType.Pet;

    private const byte EquipmentList = (byte)InventoryContainerType.Equipment;

    /// <summary>
    /// Off and back on again, and it is still the same creature at every step.
    /// </summary>
    /// <remarks>
    /// The identity is read at each of the four stops rather than only at the end, so a round trip
    /// that destroyed the creature and made a new one indistinguishable from it fails here instead
    /// of passing on the strength of the last two reads agreeing.
    /// </remarks>
    [SkippableFact]
    public async Task A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await WithACharacterWearingNothingAsync(async fixture =>
        {
            await fixture.SeedIntoBagAsync(BagSlot);
            var seeded = await fixture.IdentityAtAsync(PetList, BagSlot);
            Assert.NotNull(seeded);

            var puttingOn = await fixture.MoveAsync(PetList, BagSlot, EquipmentList, CreatureSlot);
            Assert.Equal(ItemMoveStatus.Moved, puttingOn.Status);
            Assert.Equal(ContractCreatureItemId, puttingOn.ItemId);
            Assert.Equal(seeded, await fixture.IdentityAtAsync(EquipmentList, CreatureSlot));
            Assert.Null(await fixture.IdentityAtAsync(PetList, BagSlot));

            var takingOff = await fixture.MoveAsync(EquipmentList, CreatureSlot, PetList, BagSlot);
            Assert.Equal(ItemMoveStatus.Moved, takingOff.Status);
            Assert.Equal(seeded, await fixture.IdentityAtAsync(PetList, BagSlot));
            Assert.Null(await fixture.IdentityAtAsync(EquipmentList, CreatureSlot));

            // The one this whole test exists for. Before the creature's name was in the slot table
            // this answered NotWearable, and the creature stayed in the bag for good.
            var puttingBackOn = await fixture.MoveAsync(
                PetList, BagSlot, EquipmentList, CreatureSlot);
            Assert.Equal(ItemMoveStatus.Moved, puttingBackOn.Status);
            Assert.Equal(ContractCreatureItemId, puttingBackOn.ItemId);
            Assert.Equal(seeded, await fixture.IdentityAtAsync(EquipmentList, CreatureSlot));
            Assert.Null(await fixture.IdentityAtAsync(PetList, BagSlot));
        });
    }

    /// <summary>
    /// Aiming a creature at a slot that is not the creature's is still refused.
    /// </summary>
    /// <remarks>
    /// Naming the creature in the slot table could have been done by letting anything reach any
    /// worn slot, and that would pass the test above while dressing a character in a creature
    /// wherever the request asked for. The slot is checked, and it is checked against the archive's
    /// answer rather than against the request.
    /// </remarks>
    [SkippableFact]
    public async Task A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await WithACharacterWearingNothingAsync(async fixture =>
        {
            await fixture.SeedIntoBagAsync(BagSlot);
            var seeded = await fixture.IdentityAtAsync(PetList, BagSlot);

            var aimedAtTheShoulder = await fixture.MoveAsync(PetList, BagSlot, EquipmentList, 15);

            Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, aimedAtTheShoulder.Status);
            Assert.Equal(seeded, await fixture.IdentityAtAsync(PetList, BagSlot));
            Assert.Null(await fixture.IdentityAtAsync(EquipmentList, 15));
        });
    }

    [SkippableFact]
    public async Task Legacy_worn_creature_is_normalized_once_without_replacing_its_world_row()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await WithACharacterWearingNothingAsync(async fixture =>
        {
            await fixture.SeedLegacyWornCreatureAsync();
            var identity = await fixture.IdentityAtAsync(EquipmentList, 24);
            Assert.NotNull(identity);

            await fixture.NormalizeLegacyWornAsync();
            Assert.Null(await fixture.IdentityAtAsync(EquipmentList, 24));
            Assert.Equal(identity, await fixture.IdentityAtAsync(EquipmentList, 25));

            await fixture.NormalizeLegacyWornAsync();
            Assert.Equal(identity, await fixture.IdentityAtAsync(EquipmentList, 25));
        });
    }

    private static async Task WithACharacterWearingNothingAsync(Func<Fixture, Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-creature-worn-{Guid.NewGuid():N}.db");
        var content = ContentArchive.CreateContentProvider();
        try
        {
            await body(new Fixture(path, content));
        }
        finally
        {
            content.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    /// <summary>
    /// One character with a bag and an equipment container, over one database and one archive.
    /// </summary>
    /// <remarks>
    /// Both containers are the persistent one and both are registered on the same store, which is
    /// what putting a piece on is: two containers, one settlement. The rules handed to the move are
    /// built from the production content registration, so the slot a creature is worn in is the
    /// archive's answer and not a fixture's.
    /// </remarks>
    private sealed class Fixture
    {
        private readonly string path;
        private readonly IItemDomainStore store;

        private readonly CharacterId character = new(Character);

        private readonly DomainTransactionScope scope = new();

        private readonly LegacyWornItemNormalizer normalizer;

        private readonly DirectCommandBus bus;

        public Fixture(string databasePath, ServiceProvider content)
        {
            this.path = databasePath;
            _ = new SqliteDatabase(databasePath, this.scope);

            var rules = new ItemPlacementRuleService(
                content.GetRequiredService<IItemPlacementDefinitionSource>(),
                content.GetRequiredService<IEquipmentSlotDefinitionSource>());
            var store = this.store = new SqliteItemDomainStore(databasePath);
            this.normalizer = new LegacyWornItemNormalizer(store, rules,
                new Dfo.Server.Application.Characters.AmbientStoreAccess(this.scope));
            var containers = new CharacterContainerDomainService();
            foreach (var type in new[]
                     {
                         InventoryContainerType.Main, InventoryContainerType.Equipment,
                         InventoryContainerType.Pet,
                     })
            {
                containers.Register(this.character, new PersistentItemContainer(
                    ContainerId.ForCharacter(this.character, type),
                    new ContainerShape(type, 0),
                    store,
                    this.scope,
                    rules,
                    new ItemSlotRuleService()));
            }

            this.Containers = containers;
            var mappings = new InventorySlotMappingRuleService();
            this.bus = new DirectCommandBus(
                move: new MoveItemSpaceCommandHandler(
                    new BusinessInventorySlotDomainService(containers, mappings),
                    new ItemMovementDomainService(
                        rules, new ItemSlotRuleService(), new CreatureGoesBackOnTestsWearEligibility())),
                scope: this.scope);
        }

        public CharacterContainerDomainService Containers { get; }

        public async Task SeedLegacyWornCreatureAsync()
        {
            var equipment = this.Containers.FindContainer(this.character, InventoryContainerType.Equipment)!;
            await using var lease = this.scope.Begin();
            var session = store.SessionIn(scope.Current!);
            await session.PutAsync(equipment.Id, 24, await session.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(CreatureKind, ContractCreatureItemId, 1), false,
                creature: new(7, 1, 42, 2, 3, 9, [1, 2, 3], 4)), default), default);
            await lease.CommitAsync(CancellationToken.None);
        }

        public async Task NormalizeLegacyWornAsync()
        {
            await using var lease = this.scope.Begin();
            await this.normalizer.NormalizeAsync(this.character, CancellationToken.None);
            await lease.CommitAsync(CancellationToken.None);
        }

        /// <summary>Puts the creature in the bag the way anything else would arrive there.</summary>
        public async Task SeedIntoBagAsync(int slotIndex)
        {
            var bag = this.Containers.FindContainer(this.character, InventoryContainerType.Pet)!;
            await using var lease = this.scope.Begin();
            var session = store.SessionIn(scope.Current!);
            await session.PutAsync(bag.Id, slotIndex, await session.CreateAsync(ItemSnapshot.FromCore(
                StackableItemCore.Create(CreatureKind, ContractCreatureItemId, 1), false,
                creature: new(100, 0, 0, 0, 0, 1, [], 0)), default), default);
            await lease.CommitAsync(CancellationToken.None);
        }

        public ValueTask<ItemMoveResult> MoveAsync(
            byte sourceList, short sourceSlot, byte destinationList, short destinationSlot) =>
            this.bus.SendAsync(
                new MoveItemSpaceCommand(
                    this.character,
                    new InventorySlotMappingRuleService().ToBusinessSlot(
                        new GameSlot((InventoryContainerType)sourceList, sourceSlot)),
                    0,
                    new InventorySlotMappingRuleService().ToBusinessSlot(
                        new GameSlot((InventoryContainerType)destinationList, destinationSlot))),
                CancellationToken.None);

        /// <summary>
        /// The name of the thing standing in a position, or nothing when none is.
        /// </summary>
        /// <remarks>
        /// Read out of the slot table, which is where a position says which world row it holds. The
        /// world row's own key is read alongside it and the two are required to agree, so a move
        /// that rebuilt one of the two tables and not the other cannot read as an identity kept.
        /// </remarks>
        public async Task<long?> IdentityAtAsync(byte listType, int slotIndex)
        {
            await using var connection = new SqliteConnection($"Data Source={this.path}");
            await connection.OpenAsync();

            await using var slots = connection.CreateCommand();
            slots.CommandText = """
                SELECT id FROM item_slots
                WHERE owner_scope = 'character' AND owner_id = @owner
                  AND list_type = @list AND slot_index = @slot AND kind = 'world';
                """;
            Bind(slots, listType, slotIndex);
            var named = await slots.ExecuteScalarAsync() as long?;

            await using var rows = connection.CreateCommand();
            rows.CommandText = """
                SELECT core_uid FROM item_slots
                WHERE owner_scope = 'character' AND owner_id = @owner
                  AND list_type = @list AND slot_index = @slot;
                """;
            Bind(rows, listType, slotIndex);
            Assert.Equal(named, await rows.ExecuteScalarAsync() as long?);

            return named;
        }

        private static void Bind(SqliteCommand command, byte listType, int slotIndex)
        {
            command.Parameters.AddWithValue("@owner", Character);
            command.Parameters.AddWithValue("@list", (int)listType);
            command.Parameters.AddWithValue("@slot", slotIndex);
        }
    }
}

internal sealed class CreatureGoesBackOnTestsWearEligibility : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
