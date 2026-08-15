using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AccountRoster;

/// <summary>
/// Decodes the GET_USERINFO (0x0008) body <c>{ u16 target, u8 mode }</c>.
/// The mode byte is a wire-level discriminator: one type carries two semantically distinct
/// queries, so each mode maps to its own typed message and the mode itself is never passed on
/// to the application layer. Layout is instruction-confirmed from all five client builder sites;
/// see protocol/game/client-to-server/0x0008.md.
/// </summary>
internal static class GetUserInfoPayloadCodec
{
    /// <summary>Every client builder site writes exactly one u16 and one u8, so any other
    /// length is not this message.</summary>
    public const int BodyLength = 3;

    /// <summary>Written at offset 0 when the query has no specific target. The client rejects it
    /// as a real target id (targeted queries are gated to 0..0xFFFE), so it must not be used for
    /// lookups.</summary>
    public const ushort NoTarget = 0xFFFF;

    /// <summary>Account-scope query answered with the character roster notification.</summary>
    private const byte AccountRosterMode = 0x02;

    public static IClientNetworkMessage? Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            return null;
        }

        // Read but do not forward: in account-roster mode the target is the NoTarget sentinel and
        // plays no part in locating the account. Reading it keeps the offset accounted for and
        // documents why it is dropped rather than leaving the field unexplained.
        var target = BinaryPrimitives.ReadUInt16LittleEndian(body);

        return body[2] switch
        {
            AccountRosterMode when target == NoTarget => new GetAccountCharacterRosterRequest(metadata),
            _ => null,
        };
    }
}
