using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
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
/// What a thing is, answered by the archive, on the way into storage.
/// </summary>
/// <remarks>
/// The kind byte a row carries and the archive's reading of the same template do not agree, and
/// nothing had ever compared them: an avatar is stored as kind 8 while the archive's equipment
/// index gives it kind 1. Because a projection can still assemble the bytes the client expects out
/// of either, getting this wrong changes nothing anybody would notice until something starts
/// depending on it -- which is exactly what the world side is about to do.
///
/// The contract creature was a second such pair and is no longer one: the archive now gives it the
/// kind storage records it under. What that repair does not touch is any of this -- the archive is
/// still the only thing asked, and a row's byte is still not an input.
///
/// So these run against the operator's own archive, through the production content registration
/// and a real store. What they hold is that the answer comes from the template and could not have
/// come from the row: every case here writes a kind byte that a reading of the row would classify
/// differently from the way the archive classifies the template.
/// </remarks>
public sealed class ItemClassificationTests
{
    /// <summary>
    /// <c>equipment/creature/faras.equ</c>, whose <c>[equipment type]</c> is <c>[creature]</c>.
    /// </summary>
    private const int ContractCreatureItemId = 63000;

    /// <summary>
    /// <c>stackable/event/chn_southerndale_dungeon_2011/chn_invitation_secret.stk</c>: a pile.
    /// </summary>
    private const int StackableItemId = 2680738;

    /// <summary>What the archive's equipment index gives an ordinary worn piece.</summary>
    private const byte EquipmentKindByte = 1;

    /// <summary>What the archive gives the contract creature, and what storage records it as.</summary>
    private const byte CreatureKindByte = 5;

    /// <summary>An ordinary consumable's kind byte.</summary>
    private const byte ConsumableKindByte = 2;

    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(77), InventoryContainerType.Main);

    /// <summary>
    /// The archive reads the contract creature as a creature, and a pile as a pile.
    /// </summary>
    /// <remarks>
    /// Stated on its own so that the cases below are known to be exercising two different answers
    /// rather than agreeing with everything for the wrong reason. The kind byte asserted here is
    /// the archive's own, which is the creature's 5 since the kind repair; that repair is asserted
    /// where it belongs, in <c>CreatureKindAgreesWithTheArchiveTests</c>, and is named here only so
    /// that this case cannot silently start passing on a kind nobody reads a creature by.
    /// </remarks>
    [SkippableFact]
    public async Task The_archive_reads_the_contract_creature_as_a_creature_and_not_as_the_row_does()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var rules = ArchiveRules(out var content);
        using (content)
        {
            var placement = await rules.PlacementForAsync(
                ContractCreatureItemId, CancellationToken.None);

            Assert.NotNull(placement);
            Assert.Equal(CreatureKindByte, placement!.ItemKind);
            Assert.Equal(
                ItemWorldClass.Creature,
                await rules.ClassifyAsync(ContractCreatureItemId, CancellationToken.None));
            Assert.Equal(
                ItemWorldClass.Interchangeable,
                await rules.ClassifyAsync(StackableItemId, CancellationToken.None));
        }
    }

    /// <summary>
    /// An item whose stored kind disagrees with the archive is classified as the archive reads it.
    /// </summary>
    /// <remarks>
    /// Two writes, and each one's kind byte points away from the archive's answer: the creature is
    /// written carrying an ordinary consumable's kind, and the pile is written carrying the kind
    /// the equipment index hands out. A classification that read the row would answer
    /// interchangeable for the first and equipment for the second, which is the opposite of both.
    /// </remarks>
    [SkippableFact]
    public async Task A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var rules = ArchiveRules(out var content);
        using (content)
        {
            var path = Path.Combine(
                Path.GetTempPath(), $"dfo-classification-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var scope = new DomainTransactionScope();
            var store = new SqliteItemDomainStore(path);
            var container = new PersistentItemContainer(
                Main,
                new CharacterId(77),
                new ContainerShape(InventoryContainerType.Main, 0),
                store,
                scope,
                null!,
                rules,
                null!,
                null!);

            await using var lease = scope.Begin();
            await container.PutAsync(
                65, Core(ConsumableKindByte, ContractCreatureItemId), CancellationToken.None);
            await container.PutAsync(
                66, Core(EquipmentKindByte, StackableItemId), CancellationToken.None);

            var session = store.SessionIn(scope.Current!);
            var creature = await session.ReadAsync(Main, 65, CancellationToken.None);
            var pile = await session.ReadAsync(Main, 66, CancellationToken.None);

            Assert.Equal(ConsumableKindByte, creature!.Core.ItemKind);
            Assert.Equal(ItemWorldClass.Creature, creature.WorldClass);
            Assert.Equal(EquipmentKindByte, pile!.Core.ItemKind);
            Assert.Equal(ItemWorldClass.Interchangeable, pile.WorldClass);
        }
    }

    private static ItemPlacementRuleService ArchiveRules(out ServiceProvider content)
    {
        content = ContentArchive.CreateContentProvider();
        return new ItemPlacementRuleService(
            content.GetRequiredService<IItemPlacementDefinitionSource>(),
            content.GetRequiredService<IEquipmentSlotDefinitionSource>());
    }

    private static InventoryItemCoreState Core(byte itemKind, int itemId) =>
        StackableItemCore.Create(itemKind, itemId, 1);
}
