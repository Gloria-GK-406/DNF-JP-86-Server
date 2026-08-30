using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// What the archive says may be produced, against the archive.
/// </summary>
/// <remarks>
/// Named items, both kinds: ones known to be in and ones known to be out. A pool built by the
/// wrong rule is still a pool, and only an item that should not be in it says so.
/// </remarks>
public sealed class PvfDropItemPoolSourceTests
{
    /// <summary>(魔法封印) 钢铁念珠 设计图 -- grade 57, rare, creation rate 1000.</summary>
    private const int SteelBeadRecipe = 10087807;

    /// <summary>遗留的水晶碎片, a material that declares no creation rate.</summary>
    private const int CrystalShard = 10088621;

    /// <summary>精灵气息, the same.</summary>
    private const int SpiritBreath = 10088620;

    /// <summary>铁脊蛇矛, one of A21's level-90 epics.</summary>
    private const int IronSpineSpear = 114000088;

    /// <summary>粗布衬衫, the level-one shirt every character starts near.</summary>
    private const int RagclothShirt = 10000;

    /// <summary>
    /// A stackable that declares a creation rate is in its bucket, at the weight it declares.
    /// </summary>
    [SkippableFact]
    public void A_declared_creation_rate_puts_a_stackable_in_its_bucket()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Stackable);

        Assert.Contains(
            new DropPoolEntry(SteelBeadRecipe, 1000),
            pool.Bucket(57, DropRarity.Rare));
    }

    /// <summary>
    /// An item that declares no creation rate is in no bucket at all.
    /// </summary>
    /// <remarks>
    /// <b>This is the assertion that says the gate is the item's own.</b> Both of these are
    /// materials of grade one and rarity one, both are among the commonest things a player holds,
    /// and both are absent from every bucket -- which is why打怪 yields recipes and materials come
    /// from somewhere else. A pool built by any rule other than the item's own declaration would
    /// have swept them in.
    /// </remarks>
    [SkippableFact]
    public void An_item_that_declares_no_creation_rate_is_in_no_bucket()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropItemPoolSource(provider);

        foreach (var family in new[] { DropItemFamily.Stackable, DropItemFamily.Equipment })
        {
            var pool = source.Load(family);
            for (var grade = 0; grade <= 100; grade++)
            {
                foreach (var rarity in Enum.GetValues<DropRarity>())
                {
                    Assert.DoesNotContain(
                        pool.Bucket(grade, rarity),
                        entry => entry.ItemId is CrystalShard or SpiritBreath);
                }
            }
        }
    }

    /// <summary>The stackable pool is the small, recipe-shaped thing the archive describes.</summary>
    /// <remarks>
    /// 774 of 42272. If this number moves a long way, the gate has changed meaning.
    /// </remarks>
    [SkippableFact]
    public void The_stackable_pool_is_the_size_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Stackable);

        Assert.Equal(774, pool.Count);
    }

    /// <summary>
    /// The equipment pool is built from the equipment files, not from the encyclopedia.
    /// </summary>
    /// <remarks>
    /// 6062 of 118479 equipment files declare a creation rate above zero. The encyclopedia index
    /// the reference server reads instead holds 13474 rows in total and passes 4362 of them, so
    /// this count cannot be reached by reading that file however its columns are interpreted.
    /// <para>
    /// The named piece is 铁脊蛇矛, an epic, which pins the rarity as the item's own rather than
    /// the encyclopedia's display class -- the two disagree about exactly this item.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_equipment_pool_comes_from_the_equipment_files()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Equipment);

        Assert.Equal(6062, pool.Count);
        Assert.Contains(
            pool.Bucket(91, DropRarity.Epic),
            entry => entry.ItemId == IronSpineSpear);

        // And its weights are the items' own, which the encyclopedia's column cannot carry: that
        // column takes only zero and one.
        Assert.Contains(pool.Bucket(91, DropRarity.Epic), entry => entry.Weight > 1);
    }

    /// <summary>
    /// A piece of equipment that declares no creation rate stays out, however ordinary it is.
    /// </summary>
    [SkippableFact]
    public void The_starting_shirt_is_not_creatable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Equipment);

        Assert.DoesNotContain(
            pool.Bucket(1, DropRarity.Common),
            entry => entry.ItemId == RagclothShirt);
    }

    /// <summary>
    /// Nothing above epic is creatable, which is what leaves the ladder's top band empty.
    /// </summary>
    /// <remarks>
    /// Worth pinning rather than assuming: the ladder declares six bands and the archive fills
    /// five of them. A future archive that starts declaring chronicle gear creatable would change
    /// what a drop can be, and this is where that would show.
    /// </remarks>
    [SkippableFact]
    public void Nothing_above_epic_declares_itself_creatable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropItemPoolSource(provider);

        foreach (var family in new[] { DropItemFamily.Equipment, DropItemFamily.Stackable })
        {
            var pool = source.Load(family);
            for (var grade = 0; grade <= 200; grade++)
            {
                Assert.Empty(pool.Bucket(grade, DropRarity.Chronicle));
                Assert.Empty(pool.Bucket(grade, DropRarity.Legendary));
            }
        }
    }

    /// <summary>A band gathers every grade it spans.</summary>
    [SkippableFact]
    public void A_band_gathers_every_grade_it_spans()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Stackable);

        var band = pool.Band(new ItemGradeBand(56, 58), DropRarity.Rare);

        Assert.Equal(
            pool.Bucket(56, DropRarity.Rare).Count
                + pool.Bucket(57, DropRarity.Rare).Count
                + pool.Bucket(58, DropRarity.Rare).Count,
            band.Count);
        Assert.Contains(band, entry => entry.ItemId == SteelBeadRecipe);
    }

    /// <summary>The family is walked once and held.</summary>
    [SkippableFact]
    public void A_pool_is_read_once_and_held()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropItemPoolSource(provider);

        Assert.Same(
            source.Load(DropItemFamily.Stackable),
            source.Load(DropItemFamily.Stackable));
    }

    /// <summary>
    /// A piece of equipment carries the durability its own file declares.
    /// </summary>
    /// <remarks>
    /// <b>Zero is not "unspecified", it is broken.</b> The declared floor drops have read this out
    /// of the archive since the room description was built; a monster's drop reached the bag with
    /// a hard-coded zero until 2026-08-30, which would have handed the player a broken weapon and
    /// looked, from the wire, exactly like a working one -- the kill frame has no durability field
    /// to disagree with.
    /// </remarks>
    [SkippableFact]
    public void A_dropped_piece_carries_the_durability_its_own_file_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Equipment);

        // 铁脊蛇矛 declares [durability] 45 and [creation rate] 125.
        var spear = pool.Bucket(91, DropRarity.Epic)
            .Single(entry => entry.ItemId == IronSpineSpear);
        Assert.Equal(45, spear.Durability);
        Assert.Equal(125, spear.Weight);

        // 4218 of the pool's 6062 carry one: the weapons and the six armour slots. The other
        // 1844 are rings, amulets, bracelets, magic stones, support items and artifacts, which
        // declare no durability at all -- zero is right for those and would be a broken piece for
        // the rest, which is why this is a count rather than a blanket assertion.
        var carried = 0;
        var total = 0;
        for (var grade = 0; grade <= 200; grade++)
        {
            foreach (var rarity in Enum.GetValues<DropRarity>())
            {
                foreach (var entry in pool.Bucket(grade, rarity))
                {
                    total++;
                    if (entry.Durability > 0)
                        carried++;
                }
            }
        }

        Assert.Equal(6062, total);
        Assert.Equal(4218, carried);
    }
}
