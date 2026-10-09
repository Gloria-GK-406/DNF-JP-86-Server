using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

/// <summary>What a character is drawn wearing, worked out from what the character wears.</summary>
/// <remarks>
/// Three places draw a character: the one inside the game, from the NOTI-2 subtype-zero frame, the
/// character-selection roster, and the roster frame inside the entry burst. All three draw the same
/// twenty-three-byte part -- slot, display item, a four-byte expansion behind its own length, a
/// state byte, a link item, an enchant value and a flag -- and they used to work it out separately:
/// the in-world frame derived it from the equipment list inside its own byte-writing loop, and both
/// rosters read a blob nothing had written since the character was created. This is that
/// derivation, once, as values rather than as bytes, so that all of them can ask it.
/// <para>
/// The two rosters were moved onto it one at a time -- the selection screen first, the entry burst
/// afterwards -- so a reader of an intermediate revision will find them disagreeing.
/// </para>
/// <para>
/// <b>It is here rather than beside either caller because neither can see the other.</b>
/// <see cref="Dfo.Server.Data.Game.CharacterRosterAppearanceState"/> is a data record, the roster
/// asks from <c>Application</c>, and the frame writes from <c>Protocol</c> -- and
/// <c>Application</c> never references <c>Protocol</c>. A static function over data records,
/// touching no content source and no I/O, is the bottom tier of the three this repository
/// orchestrates through ("纯规则：静态类，纯函数，不碰 I/O" in <c>AGENTS.md</c>), and it sits with
/// the other three of them under <c>Characters/</c>.
/// </para>
/// <para>
/// <b>This is the first thing in <c>Protocol</c> that calls a rule out of <c>Application</c>, so
/// the reason is written down rather than left to be inferred.</b> The direction is the allowed one
/// -- <c>Protocol</c> references <c>Application</c>, never the reverse -- and every slice already
/// depends on <c>Application</c> for its command bus. The other place this could have gone is
/// <c>Dfo.Server.Data</c>, which both callers also see; it did not go there because that project is
/// pinned as pure models, and behaviour in it would make that description false. Being the first of
/// its kind is not the same as being irregular, and a second one needs no further justification
/// than this one.
/// </para>
/// <para>
/// <b>What it does not decide.</b> Avatar colour and its copied appearance come from the worn value. A zero
/// avatar appearance draws its original transparent asset, never an invalid item zero. The appearance count describes the parts actually present;
/// adding a title therefore adds one part without changing any part's wire layout.
/// Whether a worn slot holds the creature (A21 slot 25) is a different question with a different
/// answer, and the frame that cares asks it separately.
/// </para>
/// </remarks>
public static class DrawnAppearance
{
    /// <summary>The end of the run of worn slots a drawn appearance is taken from.</summary>
    /// <remarks>
    /// A21's worn wire slots are avatars 0..11, weapon 12 and title 13. This agrees with
    /// InventorySlotMappingRuleService and upstream EquipmentTypeInfo.IsA21RosterAppearanceSlot
    /// (source reference, not a claim of live-client verification).
    /// </remarks>
    private const int LastDrawnSlot = 13;

    /// <summary>A21's support weapon slot, outside the contiguous appearance run.</summary>
    private const int ExtraDrawnSlot = 24;

    /// <summary>The length each part declares for its own expansion field.</summary>
    private const int ExpansionLength = 4;

    /// <summary>Whether what a character is drawn as depends on what is worn in this slot.</summary>
    /// <remarks>
    /// It answers about a <b>worn</b> slot. A slot number from the bag or from the avatar page
    /// means something else entirely, and the caller is the one that knows which list it holds.
    /// </remarks>
    public static bool DrawsFrom(int wornSlotIndex) =>
        (wornSlotIndex >= 0 && wornSlotIndex <= LastDrawnSlot) || wornSlotIndex == ExtraDrawnSlot;

    /// <summary>The parts a character wearing these is drawn as, in slot order.</summary>
    /// <remarks>
    /// Ordered here rather than by the caller: the two callers read their items out of different
    /// stores, and a picture that depends on which store answered first is a picture that drifts.
    /// </remarks>
    public static IReadOnlyList<CharacterRosterAppearanceState> Of(
        IReadOnlyList<InventoryItemState> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        return
        [
            .. worn
                .Where(item => DrawsFrom(item.SlotIndex))
                .OrderBy(item => item.SlotIndex)
                .Select(Part),
        ];
    }

    /// <summary>One worn piece as the part that draws it.</summary>
    /// <remarks>
    /// The state byte packs two facts the client draws from: the piece's attribute in its low five
    /// bits, shifted up by one, and whether the piece is amplified in the bit that vacates. Both
    /// halves are what the in-world frame has always written, and the same expression is what the
    /// upstream server writes (read from upstream source).
    /// </remarks>
    private static CharacterRosterAppearanceState Part(InventoryItemState item) => new(
        checked((byte)item.SlotIndex),
        AvatarDisplay(item),
        ExpansionLength,
        Colors(item),
        checked((byte)(((item.Core.Attribute & 0x1F) * 2) + (item.Core.AmplifyType != 0 ? 1 : 0))),
        LinkItemId: AvatarDisplay(item) != item.Core.ItemId ? item.Core.ItemId : 0,
        EnchantValue: 0u,
        Flag20: item.Core.EnchantUpgradeCount);

    private static int AvatarDisplay(InventoryItemState item) => item.Core.ItemKind == 8
        ? (item.Core.AppearanceItemId ?? item.AvatarDetail?.ClearAvatarId) is > 0 and var appearance ? appearance : item.Core.ItemId
        : item.Core.ItemId;

    private static byte[] Colors(InventoryItemState item)
    {
        var bytes = new byte[ExpansionLength];
        if (item.Core.ItemKind == 8 && item.AvatarDetail is { } avatar)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes, avatar.Color1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), avatar.Color2);
        }
        return bytes;
    }
}
