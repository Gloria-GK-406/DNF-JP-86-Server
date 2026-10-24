using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// A drag inside the avatar list, carried out by a container that really keeps its rows.
/// </summary>
/// <remarks>
/// The list's admission rule and the dispatch that reaches it are asserted elsewhere, each against
/// something small enough to be exact. What neither of those can say is whether the move happens:
/// a container standing in for the real one decides nothing and stores nothing, so a case built on
/// one proves that a request arrived and stops there.
/// <para>
/// These go through <c>PersistentItemContainer</c> with a real <c>ContainerShape</c> for the avatar
/// list and a real store behind it. That is the combination nothing else in the suite builds --
/// every other shape in these tests is the main list or the worn one -- and it is where the change
/// that made a container consult its own rule instead of the bag's actually lands.
/// </para>
/// </remarks>
public sealed class MovingInsideTheAvatarListTests
{
    private const int Account = 91;

    private const int Character = 97;

    /// <summary>A costume the archive declares: kind 8, world class avatar.</summary>
    private const int CostumeItemId = 413500000;

    private const int OtherCostumeItemId = 413550000;

    /// <summary>An ordinary consumable, to stand for everything the avatar list turns away.</summary>
    private const int PotionItemId = 1000;

    [SkippableFact]
    public async Task A_costume_moves_into_an_empty_slot_and_the_store_says_so()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithAnAvatarListAsync(content, async side =>
        {
            var avatars = List(side);
            await side.InUseCaseAsync(token => SeedAsync(side, avatars, 0, CostumeItemId, token));

            var moved = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.Avatar, 7), 1, token));

            Assert.Equal(new ItemMoveOutcome.Moved(1, CostumeItemId), moved);
            var (source, destination) = await side.InUseCaseAsync(async token => (
                await avatars.GetAsync(0, token).ConfigureAwait(false),
                await avatars.GetAsync(7, token).ConfigureAwait(false)));
            Assert.Null(source);
            Assert.Equal(CostumeItemId, destination!.ItemId);
        });
    }

    [SkippableFact]
    public async Task Two_costumes_trade_places()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithAnAvatarListAsync(content, async side =>
        {
            var avatars = List(side);
            await side.InUseCaseAsync(async token =>
            {
                await SeedAsync(side, avatars, 0, CostumeItemId, token).ConfigureAwait(false);
                await SeedAsync(side, avatars, 3, OtherCostumeItemId, token).ConfigureAwait(false);
            });

            var moved = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.Avatar, 3), 1, token));

            Assert.Equal(new ItemMoveOutcome.Moved(1, CostumeItemId), moved);
            var (first, second) = await side.InUseCaseAsync(async token => (
                await avatars.GetAsync(0, token).ConfigureAwait(false),
                await avatars.GetAsync(3, token).ConfigureAwait(false)));
            Assert.Equal(OtherCostumeItemId, first!.ItemId);
            Assert.Equal(CostumeItemId, second!.ItemId);
        });
    }

    [SkippableFact]
    public async Task Dragging_from_an_empty_slot_moves_nothing()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithAnAvatarListAsync(content, async side =>
        {
            var avatars = List(side);

            var moved = await side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.Avatar, 7), 1, token));

            Assert.Equal(new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty), moved);
        });
    }

    /// <summary>
    /// The list's own rule is the one the container consults, not the bag's.
    /// </summary>
    /// <remarks>
    /// This is the case that tells the change apart from the code it replaced. The potion's band is
    /// <c>65..120</c>, so under the main list's rule -- the one this container used to consult
    /// whatever list it was -- slot 70 would be allowed and slot 7 refused. Under the avatar list's
    /// own rule the potion is refused everywhere, because the list takes costumes. Both slots are
    /// asserted so that a container gone back to reading the main band fails on the first of them.
    /// </remarks>
    [SkippableTheory]
    [InlineData(7)]
    [InlineData(70)]
    public async Task The_avatar_list_turns_away_what_is_not_a_costume(int targetSlot)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithAnAvatarListAsync(content, async side =>
        {
            var avatars = List(side);
            await side.InUseCaseAsync(token => SeedAsync(side, avatars, 0, PotionItemId, token));

            var moved = await side.InUseCaseAsync(
                token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.Avatar, targetSlot), 1, token));

            Assert.Equal(new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot), moved);
            var stayed = await side.InUseCaseAsync(token => avatars.GetAsync(0, token));
            Assert.Equal(PotionItemId, stayed!.ItemId);
        });
    }

    /// <summary>Two hundred and ten slots, so the two hundred and tenth is past the end.</summary>
    [SkippableFact]
    public async Task A_costume_does_not_move_past_the_end_of_the_list()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithAnAvatarListAsync(content, async side =>
        {
            var avatars = List(side);
            await side.InUseCaseAsync(token => SeedAsync(side, avatars, 0, CostumeItemId, token));

            await Assert.ThrowsAsync<InventorySlotMappingException>(() =>
                side.InUseCaseAsync(token => side.MoveAsync(new CharacterId(Character), new(BusinessInventorySpace.Avatar, 0), new(BusinessInventorySpace.Avatar, 210), 1, token)));

            var stayed = await side.InUseCaseAsync(token => avatars.GetAsync(0, token));
            Assert.Equal(CostumeItemId, stayed!.ItemId);
        });
    }

    private static IItemContainer List(InventorySide side) =>
        side.Containers.GetContainer(new CharacterId(Character), InventoryContainerType.Avatar);

    private static async ValueTask<ItemReference> SeedAsync(InventorySide side, IItemContainer container,
        int index, int itemId, CancellationToken ct)
    {
        // Direct storage seeding also permits the deliberately misplaced potion used by refusal tests.
        var items = side.Store.SessionIn(side.Scope.Current!);
        var isPile = itemId == PotionItemId;
        var snapshot = ItemSnapshot.FromCore(StackableItemCore.Create(isPile ? (byte)2 : (byte)8, itemId, isPile ? 1 : itemId), isPile,
            avatar: isPile ? null : new(0, 0, new byte[30], 0, 0, 0));
        return await items.PutAsync(container.Id, index, await items.CreateAsync(snapshot, ct), ct);
    }

    private static async Task WithAnAvatarListAsync(
        ServiceProvider content, Func<InventorySide, Task> body)
    {
        var placements = content.GetRequiredService<IItemPlacementDefinitionSource>();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-avatar-list-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, placements);
            await using (var connection = new SqliteConnection(SqliteTestPools.ConnectionString(path)))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'avatarlist');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'avatarlist',3,60);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            side.BindList(InventoryContainerType.Avatar, Character, placements: placements);
            await body(side);
        }
        finally
        {
            SqliteTestPools.ClearOwned(path);
            File.Delete(path);
        }
    }
}
