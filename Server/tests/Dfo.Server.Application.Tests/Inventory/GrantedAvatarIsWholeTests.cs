using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// An avatar the archive declares one is whole from the moment the delivery puts it down.
/// </summary>
/// <remarks>
/// Whole means three things at once, and any one of them missing makes the other two worthless. The
/// row has to carry the kind storage judges an avatar by, or the load hands it none of its facts
/// and the save refuses to write it back at all. It has to carry an identity of its own above zero,
/// because the load, the save and both projections each refuse one that does not. And it has to
/// carry the six facts, for the same three reasons.
/// <para>
/// So what is held here is not a value but a journey: an avatar is granted through the same
/// container verb a reward or a command uses, then read back, then written back, then read back
/// again. Every guard on that road belongs to somebody else -- none of them is this outcome's --
/// which is exactly why the journey is the assertion. A delivery that produced a half-formed
/// avatar would be refused by one of them, and before this outcome every delivery did.
/// </para>
/// <para>
/// The archive is the operator's own and the store is a real database. Stubbing the archive would
/// let the two sides agree on a number this outcome invented rather than on the number the server
/// has always used, which is the mistake the creature's repair was careful not to make.
/// </para>
/// </remarks>
public sealed class GrantedAvatarIsWholeTests
{
    /// <summary>
    /// <c>equipment.lst</c>'s <c>101550507</c>, whose <c>[equipment type]</c> ends in
    /// <c>avatar]</c>.
    /// </summary>
    /// <remarks>
    /// One of the three avatars <c>PvfDropRateTableSourceTests.No_avatar_declares_itself_creatable</c>
    /// already names out of the same list, so the identifier is one the archive is known to declare
    /// rather than one picked here.
    /// </remarks>
    private const int AvatarItemId = 101550507;

    /// <summary>
    /// The kind byte every reader and writer of an avatar row looks for.
    /// </summary>
    /// <remarks>
    /// Written as a literal rather than borrowed from the delivery, so that the delivery agreeing
    /// with itself cannot be what makes this pass. The three that demand it are
    /// <c>InventoryEntryAssembly.WithAvatarDetail</c> on the way out, the avatar scan in
    /// <c>InventoryEntryAssembly.SaveAsync</c> on the way in, and the two encoders.
    /// </remarks>
    private const byte KindStorageJudgesAnAvatarBy = 8;

    private const int Account = 84;

    private const int Character = 93;

    /// <summary>
    /// The archive gives an avatar template the kind storage judges an avatar by.
    /// </summary>
    /// <remarks>
    /// Both halves for one template: the archive is asked what kind it hands out, and a grant that
    /// went through the production path is asked what it wrote. The two are compared to each other
    /// and then to the number the entry path reads an avatar by, so neither an archive that
    /// answered something new nor a delivery that ignored the archive could leave this green.
    /// </remarks>
    [SkippableFact]
    public async Task The_archive_and_storage_answer_the_same_kind_for_one_avatar()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = ArchiveRules(content);

        var fromTheArchive = await rules.PlacementForAsync(AvatarItemId, CancellationToken.None);
        Assert.NotNull(fromTheArchive);
        Assert.Equal(
            ItemWorldClass.Avatar,
            await rules.ClassifyAsync(AvatarItemId, CancellationToken.None));
        Assert.Equal(KindStorageJudgesAnAvatarBy, fromTheArchive!.ItemKind);

        await WithASeededCharacterAsync(content, async (path, side) =>
        {
            var landed = await GrantAsync(side);
            var stored = await side.InUseCaseAsync(token =>
                new SqliteItemDomainStore(path)
                    .SessionIn(side.Scope.Current!)
                    .GetAsync(
                        ContainerId.ForCharacter(
                            new CharacterId(Character), InventoryContainerType.Avatar),
                        landed.SlotIndex,
                        token));

            Assert.NotNull(stored);
            Assert.Equal(AvatarItemId, stored!.Snapshot.ToCore().ItemId);
            Assert.Equal(fromTheArchive.ItemKind, stored.Snapshot.ToCore().ItemKind);
            Assert.Equal(KindStorageJudgesAnAvatarBy, stored.Snapshot.ToCore().ItemKind);
        });
    }

    /// <summary>
    /// A granted avatar is served, written back and served again, unchanged and unrefused.
    /// </summary>
    /// <remarks>
    /// The four steps are four use cases, the way the server dispatches them: the grant settles, the
    /// load settles, the save settles, and the second load settles. Nothing is asserted about what
    /// the delivery intended -- what is read is what the database held afterwards, twice, with a
    /// full save in between.
    /// <para>
    /// The identity is compared across the round trip rather than merely being above zero, because
    /// an avatar that came back under a different name would be a different avatar standing where
    /// the player left one.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async (_, side) =>
        {
            var landed = await GrantAsync(side);

            // Served.
            var first = await OneAvatarAsync(side, landed.SlotIndex);
            Assert.NotNull(first.Identity);
            Assert.Equal(KindStorageJudgesAnAvatarBy, first.Core.ItemKind);

            // An identity of its own, above zero, which is what three separate guards demand.
            Assert.True(first.Core.Value > 0);

            // Its own, and not the count it landed as. Above zero cannot say that by itself: the
            // field the identity goes in is the same field a pile counts itself in, and a slot
            // opened for one of anything already carries a one there. So a delivery that drew no
            // identity at all would leave "above zero" green, and this is the assertion that would
            // not be.
            Assert.NotEqual(landed.Count, first.Core.Value);

            // The six, as the delivery makes them.
            var facts = first.AvatarDetail;
            Assert.NotNull(facts);
            Assert.Equal(0, facts!.ExpireDate);
            Assert.Equal(0, facts.ClearAvatarId);
            Assert.Equal(new byte[30], facts.JewelSocket);
            Assert.Equal(0, (int)facts.Color1);
            Assert.Equal(0, (int)facts.Color2);
            Assert.Equal(0, facts.DeleteDate);

            // Committed lifecycle state is read by a new transaction; no lease writeback exists.
            // And served again: still there, still the same thing, still carrying the same six.
            var second = await OneAvatarAsync(side, landed.SlotIndex);
            Assert.Equal(first.Identity, second.Identity);
            Assert.Equal(first.Core.ItemId, second.Core.ItemId);
            Assert.Equal(first.Core.ItemKind, second.Core.ItemKind);
            Assert.Equal(first.Core.Value, second.Core.Value);

            var after = second.AvatarDetail;
            Assert.NotNull(after);
            Assert.Equal(facts.ExpireDate, after!.ExpireDate);
            Assert.Equal(facts.ClearAvatarId, after.ClearAvatarId);
            Assert.Equal(facts.JewelSocket, after.JewelSocket);
            Assert.Equal(facts.Color1, after.Color1);
            Assert.Equal(facts.Color2, after.Color2);
            Assert.Equal(facts.DeleteDate, after.DeleteDate);
        });
    }

    /// <summary>
    /// Two avatars granted one after the other carry two different identities, each one drawn.
    /// </summary>
    /// <remarks>
    /// The identity is the half of an avatar's shape that "above zero" is unable to hold. The
    /// value inside a core is what a pile counts itself in, so a slot opened for a single item
    /// arrives carrying a one there whether anything drew a number or not -- and one is above
    /// zero, so every guard that only asks for more than zero stays satisfied, and stays satisfied
    /// for a single avatar forever. Two avatars are what tell the two apart: two landed counts are
    /// the same number and two draws are not.
    /// <para>
    /// Each is also compared against the count its own grant reported, so that the case says
    /// "drawn" rather than merely "not equal to each other" -- two deliveries that both wrote the
    /// count would be caught by the first assertion, and a delivery that wrote the count into one
    /// of them by the second.
    /// </para>
    /// <para>
    /// What is compared is what the load served twice over, not what either grant said it wrote.
    /// The row's own name is compared too: two avatars standing in one bag under one name is the
    /// state the store's fill refuses, and the two numbers being different is why it does not have
    /// to.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();

        await WithASeededCharacterAsync(content, async (_, side) =>
        {
            var earlier = await GrantAsync(side);
            var later = await GrantAsync(side);
            Assert.NotEqual(earlier.SlotIndex, later.SlotIndex);

            var one = await OneAvatarAsync(side, earlier.SlotIndex);
            var other = await OneAvatarAsync(side, later.SlotIndex);

            // Drawn: neither identity is the count its own grant put down.
            Assert.NotEqual(earlier.Count, one.Core.Value);
            Assert.NotEqual(later.Count, other.Core.Value);

            // Its own: the two avatars are not each other.
            Assert.NotEqual(one.Core.Value, other.Core.Value);
            Assert.NotEqual(one.Identity, other.Identity);

            // And still above zero, which is what the load, the save and both projections demand.
            Assert.True(one.Core.Value > 0);
            Assert.True(other.Core.Value > 0);
        });
    }

    /// <summary>One avatar granted through the container verb every reward reaches.</summary>
    private static async Task<ItemPlacementOutcome.PutInEmptySlot> GrantAsync(InventorySide side)
    {
        var granted = await side.InUseCaseAsync(token => side.Grants.GrantAsync(new AccountId(Account), new CharacterId(Character),
            side.Containers, AvatarItemId, 1, token));
        return Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(granted.Placement);
    }

    /// <summary>What the entry load serves at that position of the character's main bag.</summary>
    private static async Task<InventoryItemState> OneAvatarAsync(InventorySide side, int slotIndex)
    {
        var loaded = await side.InUseCaseAsync(token =>
            new CharacterInventoryEntryDomainService(side.CharacterEntries)
                .LoadAsync(new AccountId(Account), new CharacterId(Character), token));
        var avatar = loaded.Containers.Single(
            container => container.ContainerType == InventoryContainerType.Avatar);
        return Assert.Single(avatar.Items, item => item.SlotIndex == slotIndex);
    }

    private static ItemPlacementRuleService ArchiveRules(ServiceProvider content) =>
        new(content.GetRequiredService<IItemPlacementDefinitionSource>(),
            content.GetRequiredService<IEquipmentSlotDefinitionSource>());

    /// <summary>
    /// One database with an account, a character and a bound main bag, all read by the archive.
    /// </summary>
    /// <remarks>
    /// The bag is bound before anything is granted, because that is the one line entry runs before
    /// it dispatches anything into a character's inventory; a fixture that skipped it would be
    /// exercising a state the server never reaches.
    /// </remarks>
    private static async Task WithASeededCharacterAsync(
        ServiceProvider content, Func<string, InventorySide, Task> body)
    {
        var placements = content.GetRequiredService<IItemPlacementDefinitionSource>();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-granted-avatar-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, placements);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'granted');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'granted',3,60);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            side.BindMain(Character, placements: placements);
            side.BindList(InventoryContainerType.Avatar, Character, placements: placements);
            await body(path, side);
        }
        finally
        {
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
}
