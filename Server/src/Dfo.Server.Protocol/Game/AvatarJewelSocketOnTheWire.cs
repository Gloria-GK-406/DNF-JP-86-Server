namespace Dfo.Server.Protocol.Game;

/// <summary>
/// How much of a stored jewel socket goes on the wire, and the one way it is written.
/// </summary>
/// <remarks>
/// Three frames carry an avatar's jewel socket, and every one of them writes the same shape: a
/// length, then that many bytes. Each used to carry its own copy of the length, and they drifted --
/// the item lists were corrected to A21's eighteen while the worn entry and the mailbox attachment
/// were left at the thirty that is stored. That is the reason this lives in one place rather than
/// three: the number is one fact about the wire, so a reader who changes it should not have to
/// know which three writers were supposed to agree.
/// <list type="bullet">
/// <item><description>The item lists, NOTI <c>0x000D</c> and <c>0x000E</c>.</description></item>
/// <item><description>The worn entry, NOTI <c>0x0002</c> subtype 1 and message <c>0x019F</c>.</description></item>
/// <item><description>The mailbox attachment, message <c>0x0061</c>.</description></item>
/// </list>
/// The colour pair that follows the socket is not here. It is written the same way everywhere, but
/// it never disagreed, and moving code that was already right only makes a change harder to read.
/// </remarks>
internal static class AvatarJewelSocketOnTheWire
{
    /// <summary>How much of the stored jewel socket A21 puts on the wire.</summary>
    /// <remarks>
    /// Eighteen of the thirty that are stored. The stored socket is five slots of six bytes; A21
    /// sends the first three. The reference A21 server names the figure
    /// <c>A21AvatarJewelBytes = 18</c> and all three of its writers read that one constant
    /// (<c>ItemListProtocolWriter.cs</c> lines 89 and 193, <c>MailboxHandler.cs</c> line 1127),
    /// writing it from a thirty-byte column by truncation rather than reshaping. That reading of
    /// the upstream source is the whole basis for eighteen.
    /// <para>
    /// <b>No live test can confirm this number, and none should be claimed.</b> The run declares
    /// its own length, so the client honours whatever it is given: thirty parses and aligns, and
    /// eleven avatars sent that way lined up back to back against a live client. Every jewel socket
    /// that has been tested is also all zeros, and eighteen zeros look exactly like thirty zeros.
    /// What that live run proved was the shape of the block, not the size of this field.
    /// </para>
    /// <para>
    /// There is a motive that is weaker evidence and is offered as such: the client's destination
    /// for this run was measured by static disassembly to be a stack local with far less room than
    /// the distance to its <c>/GS</c> cookie, so twelve extra bytes would land on whatever is next
    /// without failing loudly. That part is not observed live and is not what the choice rests on.
    /// </para>
    /// </remarks>
    internal const int WireJewelBytes = 18;

    /// <summary>Writes the length-prefixed jewel run: the length, then that many bytes of it.</summary>
    /// <remarks>
    /// The stored socket is thirty bytes and the wire takes the first <see cref="WireJewelBytes"/>.
    /// Anything shorter than that -- including nothing at all, which is what an item with no avatar
    /// facts has to say -- is written as zeros, matching the reference server's own fixed-width
    /// write. The callers keep their own guards on the stored length; this one only decides what
    /// reaches the wire.
    /// </remarks>
    internal static void WriteJewelRun(BinaryWriter writer, ReadOnlySpan<byte> stored)
    {
        writer.Write(WireJewelBytes);
        writer.Write(stored.Length >= WireJewelBytes
            ? stored[..WireJewelBytes]
            : new byte[WireJewelBytes]);
    }
}
