using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// That every refusal is answered, and answered with something the client either says truthfully
/// or does not say at all.
/// </summary>
/// <remarks>
/// The three tables below are the client's, recovered by disassembling each handler's branch chain
/// and decrypting <c>dstr.dat</c>; they are archived at
/// <c>protocol/patterns/data/probes/cmd-reason-messages-20260818.md</c>. They are restated here
/// because the choice of a code is only correct relative to them -- a value outside them is silence
/// whether or not that was intended, and a value inside them prints whatever sentence it owns
/// whether or not that sentence is true.
/// </remarks>
public sealed class ItemRefusalCodeTests
{
    /// <summary>Codes <c>0x002C</c>'s own handler matches, at <c>RVA 0x008D85C0</c>.</summary>
    private static readonly byte[] UseCodesTheClientMatches = [7, 8, 14, 17, 19, 23, 95, 250, 253];

    /// <summary>Of those, the ones that put a sentence in front of the player.</summary>
    private static readonly byte[] UseCodesTheClientDraws = [7, 8, 14, 19, 23, 95, 250, 253];

    /// <summary>Codes <c>0x0013</c>'s own handler matches, at <c>RVA 0x008ECCE0</c>.</summary>
    private static readonly byte[] MoveCodesTheClientMatches =
        [1, 4, 7, 8, 14, 17, 18, 19, 23, 94, 104, 114, 175, 180, 235];

    /// <summary>Of those, the ones that put a sentence on the screen rather than in the log.</summary>
    private static readonly byte[] MoveCodesTheClientDraws =
        [1, 4, 7, 8, 14, 17, 19, 23, 94, 104, 114, 175, 235];

    /// <summary>
    /// The fallback table at <c>RVA 0x01B897C0</c>, consulted by all eighty call sites.
    /// </summary>
    /// <remarks>
    /// This is why a code cannot be chosen per protocol in isolation: any type answering one of
    /// these gets the same account-security or trade-restriction sentence.
    /// </remarks>
    private static readonly byte[] SharedFallbackCodes =
        [77, 105, 114, 115, 122, 123, 134, 136, 137, 208, 210, 214, 228, 229, 230, 237, 239];

    [Fact]
    public void The_silent_use_code_is_in_none_of_the_clients_tables()
    {
        Assert.DoesNotContain(UseStackableResponse.Silent, UseCodesTheClientMatches);
        Assert.DoesNotContain(UseStackableResponse.Silent, SharedFallbackCodes);
    }

    [Fact]
    public void The_silent_move_code_is_in_none_of_the_clients_tables()
    {
        Assert.DoesNotContain(MoveItemSpaceResponse.Silent, MoveCodesTheClientMatches);
        Assert.DoesNotContain(MoveItemSpaceResponse.Silent, SharedFallbackCodes);
    }

    /// <remarks>
    /// Every refusal reason must produce an answer. A protocol that stopped answering some of them
    /// would leave the client holding its inventory closed, which is the failure that made every
    /// drag look inert before any of this existed.
    /// <para>
    /// This also catches a refusal nobody decided a code for: the translation lists each status
    /// explicitly and raises on anything else, so a new one added without a decision fails here
    /// rather than becoming silence that nobody chose.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_use_refusal_is_answered_with_a_code()
    {
        foreach (var status in Enum.GetValues<ItemUseStatus>())
        {
            if (status is ItemUseStatus.Used)
                continue;

            var code = UseStackableHandler.ErrorCodeFor(status);
            Assert.True(
                code == UseStackableResponse.Silent || UseCodesTheClientDraws.Contains(code),
                $"{status} answers with {code}, which the client neither draws nor ignores.");
        }
    }

    [Fact]
    public void Every_move_refusal_is_answered_with_a_code()
    {
        foreach (var status in Enum.GetValues<ItemMoveStatus>())
        {
            if (status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged)
                continue;

            var code = MoveItemSpaceHandler.ErrorCodeFor(status);
            Assert.True(
                code == MoveItemSpaceResponse.Silent || MoveCodesTheClientDraws.Contains(code),
                $"{status} answers with {code}, which the client neither draws nor ignores.");
        }
    }

    /// <remarks>
    /// The judgement has its own vocabulary, and it reaches the player through this translation. A
    /// verdict nobody mapped would arrive as "not a usable item" and be read out as 「非消耗品，
    /// 无法使用」 -- the same dishonesty as answering the server's own timing gap with it.
    /// </remarks>
    [Fact]
    public void Every_refusal_the_judgement_can_give_has_a_status_of_its_own()
    {
        foreach (var verdict in Enum.GetValues<ItemUsability>())
        {
            if (verdict is ItemUsability.Allowed)
                continue;

            var status = UseStackableCommandHandler.StatusFor(verdict);
            Assert.NotEqual(ItemUseStatus.Used, status);
        }

        // Each refusal keeps its own identity rather than collapsing onto one status.
        var refusals = Enum.GetValues<ItemUsability>()
            .Where(verdict => verdict is not ItemUsability.Allowed)
            .Select(UseStackableCommandHandler.StatusFor)
            .ToArray();
        Assert.Equal(refusals.Length, refusals.Distinct().Count());
    }

    /// <remarks>
    /// 「金库空间不足， 无法放入物品」 is a sentence about space. It is reserved for a target
    /// container that has none; missing sessions, bindings or items must not borrow it.
    /// </remarks>
    [Fact]
    public void Only_a_full_container_uses_the_sentence_about_space()
    {
        const byte NoFreeSpace = 4;

        foreach (var status in Enum.GetValues<ItemMoveStatus>())
        {
            if (status is ItemMoveStatus.Moved or ItemMoveStatus.Unchanged)
                continue;

            if (status == ItemMoveStatus.ContainerFull)
                Assert.Equal(NoFreeSpace, MoveItemSpaceHandler.ErrorCodeFor(status));
            else
                Assert.NotEqual(NoFreeSpace, MoveItemSpaceHandler.ErrorCodeFor(status));
        }
    }

    /// <remarks>
    /// The refusals the client can name exactly must not be collapsed onto one another. Before
    /// this, every one of them answered 23 and the player was told "not a consumable" for a level
    /// they had not reached.
    /// </remarks>
    [Fact]
    public void The_use_refusals_the_client_can_name_stay_apart()
    {
        Assert.Equal(8, UseStackableHandler.ErrorCodeFor(ItemUseStatus.JobMismatch));
        Assert.Equal(14, UseStackableHandler.ErrorCodeFor(ItemUseStatus.LevelTooLow));
        Assert.Equal(23, UseStackableHandler.ErrorCodeFor(ItemUseStatus.NotAUsableItem));
        Assert.Equal(17, MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.MergeExceedsStackLimit));
        Assert.Equal(23, MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.ItemCannotOccupySlot));
        Assert.Equal(
            23, MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.DisplacedCannotOccupySlot));
        Assert.Equal(23, MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.PartialSwapNotAllowed));
    }

    /// <remarks>
    /// The client owns no sentence for a cooldown or a place; 19 「目前无法使用物品」 is the closest
    /// true thing it can say, and it is true of both.
    /// </remarks>
    [Fact]
    public void The_cooldown_and_the_place_share_the_one_sentence_that_fits_them()
    {
        Assert.Equal(19, UseStackableHandler.ErrorCodeFor(ItemUseStatus.OnCooldown));
        Assert.Equal(19, UseStackableHandler.ErrorCodeFor(ItemUseStatus.PlaceNotAllowed));
    }

    /// <remarks>
    /// The server's own faults and lost races say nothing. Every sentence either client owns for
    /// these types blames the player, and none of these is something they did.
    /// </remarks>
    [Fact]
    public void The_servers_own_problems_say_nothing()
    {
        Assert.Equal(
            UseStackableResponse.Silent, UseStackableHandler.ErrorCodeFor(ItemUseStatus.NoSession));
        Assert.Equal(
            UseStackableResponse.Silent,
            UseStackableHandler.ErrorCodeFor(ItemUseStatus.NoBoundContainer));
        Assert.Equal(
            UseStackableResponse.Silent, UseStackableHandler.ErrorCodeFor(ItemUseStatus.SlotChanged));
        Assert.Equal(
            UseStackableResponse.Silent,
            UseStackableHandler.ErrorCodeFor(ItemUseStatus.SlotDisagrees));
        Assert.Equal(
            UseStackableResponse.Silent,
            UseStackableHandler.ErrorCodeFor(ItemUseStatus.CharacterStateUnavailable));
        Assert.Equal(
            MoveItemSpaceResponse.Silent,
            MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.NoSession));
        Assert.Equal(
            MoveItemSpaceResponse.Silent,
            MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.NoBoundContainer));
        Assert.Equal(
            MoveItemSpaceResponse.Silent,
            MoveItemSpaceHandler.ErrorCodeFor(ItemMoveStatus.BothSlotsEmpty));
    }
}
