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
/// The kind byte a row carries and the archive's reading of the same template had never been
/// compared, and a caller of a write is free to hand over a core saying anything at all. Because a
/// projection can still assemble the bytes the client expects out of either, getting this wrong
/// changes nothing anybody would notice until something starts depending on it -- which is exactly
/// what the world side is about to do.
///
/// <b>Amended; what the sentence above said before is quoted next.</b> It said the two "do not
/// agree, and nothing had ever compared them: an avatar is stored as kind 8 while the archive's
/// equipment index gives it kind 1", and went on: "The contract creature was a second such pair and
/// is no longer one: the archive now gives it the kind storage records it under." Both pairs agree
/// now -- the archive gives an avatar template the 8 storage records it under, as it already gave
/// the creature its 5 -- so there is no template left where the two sources answer differently.
/// What those repairs do not touch is any of this: the archive is still the only thing asked, and a
/// row's byte is still not an input. It is corrected rather than deleted because the pair of
/// disagreements is what these cases were written for.
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

    [SkippableFact]
    public async Task Creation_rejects_kind_bytes_that_disagree_with_the_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var rules = ArchiveRules(out var content);
        using (content)
        {
            var scope = new DomainTransactionScope();
            var store = new Dfo.Server.Persistence.Tests.Support.VolatileItemDomainStore();
            var lifecycle = new ItemLifecycleDomainService(store, scope, rules);
            await using var tx = scope.Begin();
            await Assert.ThrowsAsync<InvalidDataException>(() => lifecycle.CreateAsync(
                ItemSnapshot.FromCore(Core(ConsumableKindByte, ContractCreatureItemId), true), default).AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => lifecycle.CreateAsync(
                ItemSnapshot.FromCore(Core(EquipmentKindByte, StackableItemId), false), default).AsTask());
            Assert.Empty(await store.SessionIn(scope.Current!).GetAllAsync(Main, default));
            await tx.CommitAsync(default);
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
