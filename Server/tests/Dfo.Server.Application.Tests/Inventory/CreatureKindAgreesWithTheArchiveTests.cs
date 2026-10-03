using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Storage and the archive give the same answer for what a contract creature is.
/// </summary>
/// <remarks>
/// The second of the two behaviours this outcome was allowed to change. Two sources described the
/// same template and they did not agree: storage recorded the creature worn in slot 24 as kind 5,
/// and three readers of the entry path look for exactly that 5 -- while the archive's equipment
/// index handed every template it lists the constant 1, the creature among them. A creature that
/// arrived through the archive's reading was therefore banked as ordinary gear, and the reader of
/// the worn creature slot would look straight past it.
/// <para>
/// Both halves are asked here for one template: the archive is asked what kind it gives, and
/// storage is asked what it holds after a grant that went through the production path. Neither
/// side is stubbed -- the archive is the operator's own and the store is a real database -- because
/// a stub of either would let the two agree on a number this outcome invented rather than on the
/// one the server has always used.
/// </para>
/// </remarks>
public sealed class CreatureKindAgreesWithTheArchiveTests
{
    /// <summary>
    /// <c>equipment/creature/faras.equ</c>, whose <c>[equipment type]</c> is <c>[creature]</c>.
    /// </summary>
    private const int ContractCreatureItemId = 63000;

    /// <summary>Where a worn creature sits. Not this outcome's to decide; it reads it.</summary>
    private const int CreatureEquipSlot = 25;

    private const int Character = 77;

    /// <summary>
    /// The kind the archive gives the creature is the kind storage records it under.
    /// </summary>
    /// <remarks>
    /// Storage is not asked for a constant here. It is asked for the row a grant actually wrote,
    /// and then the entry path's own reader of the worn creature slot is handed that row: if the
    /// number the archive now gives were anything other than the one that reader looks for, the
    /// last assertion would find no creature standing in slot 24.
    /// </remarks>
    [SkippableFact]
    public async Task The_archive_and_storage_answer_the_same_kind_for_one_contract_creature()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = new ItemPlacementRuleService(
            content.GetRequiredService<IItemPlacementDefinitionSource>(),
            content.GetRequiredService<IEquipmentSlotDefinitionSource>());

        // What the archive says the template is.
        var fromTheArchive = await rules.PlacementForAsync(
            ContractCreatureItemId, CancellationToken.None);
        Assert.NotNull(fromTheArchive);
        Assert.Equal(
            ItemWorldClass.Creature,
            await rules.ClassifyAsync(ContractCreatureItemId, CancellationToken.None));

        var path = Path.Combine(Path.GetTempPath(), $"dfo-creature-kind-{Guid.NewGuid():N}.db");
        try
        {
            var scope = new DomainTransactionScope();
            _ = new SqliteDatabase(path, scope);
            var store = new SqliteItemDomainStore(path);
            var pet = ContainerId.ForCharacter(
                new CharacterId(Character), InventoryContainerType.Pet);
            var container = new PersistentItemContainer(
                pet,
                new ContainerShape(InventoryContainerType.Pet, 0),
                store,
                scope,
                rules,
                new ItemSlotRuleService());
            var registered = new CharacterContainerDomainService();
            registered.Register(new CharacterId(Character), container);
            var business = new CharacterBusinessInventoryDomainService(registered, rules,
                new ItemArrivalRuleService(), content.GetRequiredService<ICreatureDefinitionSource>(), lifecycle: new ItemLifecycleDomainService(store, scope, rules))
                .GetStore(new CharacterId(Character), BusinessInventoryStoreKind.Creature)
                .GetContainer(BusinessInventorySpace.CreatureInventory);

            await using var lease = scope.Begin();
            var granted = await business.AddAsync(
                ContractCreatureItemId, 1, CancellationToken.None);
            var landed = Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(granted.Placement);

            // What storage holds for the same template.
            var stored = await store.SessionIn(scope.Current!)
                .GetAsync(pet, landed.SlotIndex, CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal(ContractCreatureItemId, stored!.Snapshot.ToCore().ItemId);

            Assert.Equal(fromTheArchive!.ItemKind, stored.Snapshot.ToCore().ItemKind);
            Assert.True(stored.Snapshot.ToCore().Value > 0);
            await lease.CommitAsync(CancellationToken.None);
            using (var database = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                database.Open();
                using var command = database.CreateCommand();
                command.CommandText = "SELECT creature_field04 FROM items WHERE item_uid=@identity";
                command.Parameters.AddWithValue("@identity", stored.ItemUid!.Value.Value);
                Assert.Equal(100L, (long)command.ExecuteScalar()!);
            }

            // And the number the two agree on is the one the entry path reads a creature by.
            Assert.Equal(
                ContractCreatureItemId,
                new EquippedCreatureQuery().GetItemId(WornInTheCreatureSlot(stored.Snapshot.ToCore())));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    private static CharacterInventoryEntryState WornInTheCreatureSlot(
        InventoryItemCoreState core) =>
        new(new CharacterId(Character),
            [
                new InventoryContainerState(
                    InventoryContainerType.Equipment,
                    0,
                    [new InventoryItemState(new ItemIdentity(1), CreatureEquipSlot, core)]),
            ],
            [],
            new CharacterItemValueEntryState([], []),
            [],
            new CharacterRentalEntryState([]),
            new InventoryGoldLimitState(0, 0));
}
