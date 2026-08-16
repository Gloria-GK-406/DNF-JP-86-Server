namespace Dfo.Server.Protocol.Game;

/// <summary>
/// The two-byte outcome envelope that opens the body of every CMD (<c>command 0x01</c>) frame.
/// </summary>
/// <remarks>
/// The client's receive function strips this before dispatch, so no handler ever sees it. At
/// <c>RVA 0x00D89B16</c>, on the <c>command == 0x01</c> branch and for every type alike:
///
/// <code>
/// ReadU8 -> ok                       ; body byte 0, always read
/// if (ok == 0) ReadU8 -> reason      ; body byte 1, read only when ok == 0
/// ...
/// push reason / push ok / push frame
/// call dispatcher                    ; RVA 0x00D855E0
///     push context / push reason / push ok / push type
///     call handler                   ; handler sees (type, ok, reason, context)
/// </code>
///
/// So a handler's <c>[ebp+0x0C]</c> is <c>ok</c> and its <c>[ebp+0x10]</c> is <c>reason</c>, and the
/// stream the handler then reads from is positioned <em>after</em> the envelope. Outcome is a
/// property of the CMD family, not of any one message; only the payload that follows is per-type.
///
/// Two consequences that are easy to get wrong:
///
/// <list type="bullet">
/// <item>A refusal must carry its reason byte. The second <c>ReadU8</c> is unconditional once
/// <c>ok</c> is zero, and the read stream refills across frames, so a bare <c>00</c> does not
/// produce "reason 0" -- it eats the first byte of whatever arrives next and desynchronises every
/// frame after it.</item>
/// <item>Any non-zero first byte means success. A status code placed there is not read as a code;
/// it is read as "succeeded", and the handler proceeds to parse a payload that was never sent.</item>
/// </list>
///
/// NOTI (<c>command 0x00</c>) has no envelope: its branch at <c>RVA 0x00D89C62</c> reads nothing
/// before dispatching, and the whole body belongs to the handler.
/// </remarks>
internal static class GameCmdEnvelope
{
    private const byte Ok = 0x01;
    private const byte NotOk = 0x00;

    /// <summary>A success carrying no payload beyond the envelope.</summary>
    public static byte[] Accept() => [Ok];

    /// <summary>A success followed by the type's own payload.</summary>
    public static byte[] Accept(ReadOnlySpan<byte> payload)
    {
        var body = new byte[1 + payload.Length];
        body[0] = Ok;
        payload.CopyTo(body.AsSpan(1));
        return body;
    }

    /// <summary>A refusal carrying the reason code the client switches on.</summary>
    /// <remarks>
    /// <paramref name="reason"/> is not free-form. Each type's handler compares it against a fixed
    /// set and silently ignores anything outside that set, so an unlisted code produces no message
    /// and no state change -- indistinguishable, from the player's side, from a dropped frame.
    /// </remarks>
    public static byte[] Refuse(byte reason) => [NotOk, reason];
}
