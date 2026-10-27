using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AccountRoster;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CreateCharacter;

internal static class CreateCharacterPayloadCodec
{
    /// <summary>Layout: <c>u8 job</c>, <c>i32 nameLength</c>, name bytes, then a trailing byte.</summary>
    /// <remarks>
    /// The legacy server required at least one byte after the name and never read it. We do not
    /// require it: rejecting an otherwise valid creation over a trailing byte whose meaning nobody
    /// knows would turn an unknown into a failure. Its absence is recorded in the protocol notes
    /// as open rather than resolved here.
    /// </remarks>
    public static CreateCharacterRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return new CreateCharacterRequest(metadata, 0, null);
        }

        var job = body[0];
        return CharacterNameField.TryRead(body, offset: 1, out var name)
            ? new CreateCharacterRequest(metadata, job, name)
            : new CreateCharacterRequest(metadata, job, null);
    }

    /// <summary>
    /// The <see cref="GameCmdEnvelope"/> and nothing else: creation reports no payload of its own,
    /// which is why the roster has to follow a success.
    /// </summary>
    /// <remarks>
    /// The legacy server answered a full account or a malformed request with the bare byte
    /// <c>0x04</c>, and this server reproduced it on the reading that a leading non-zero byte was
    /// simply a non-success status. That reading was wrong. <c>0x04</c> is a reason code -- the
    /// handler at <c>RVA 0x008CB460</c> compares it against <c>0x02</c>, <c>0x04</c>, <c>0x12</c>,
    /// <c>0x13</c>, <c>0x16</c>, <c>0x17</c>, <c>0x18</c>, <c>0x9F</c> on its refusal path and
    /// prints string <c>0x2AA</c> for it -- and the envelope byte in front of it is what says the
    /// request failed. Sent bare, it was read as "succeeded", and the client ran the whole
    /// post-creation path for a character that does not exist.
    ///
    /// The legacy inconsistency was therefore a legacy bug, not a second wire shape.
    /// </remarks>
    public static byte[] Encode(CharacterCreationStatus status) => status switch
    {
        CharacterCreationStatus.Created => GameCmdEnvelope.Accept(),
        CharacterCreationStatus.NameTaken => GameCmdEnvelope.Refuse(0x18),
        CharacterCreationStatus.InvalidName => GameCmdEnvelope.Refuse(0x9F),
        _ => GameCmdEnvelope.Refuse(0x04),
    };
}

internal sealed class CreateCharacterMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CreateCharacterPayloadCodec.Decode(metadata, body);
}

public sealed class CreateCharacterHandler(ICommandBus commands)
    : ClientMessageHandler<CreateCharacterRequest>, IProtocolCommandMessageHandler<GameCreateCharacterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CreateCharacterRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameCreateCharacterCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class CreateCharacterResponseEncoder
    : GameCmdMessageEncoder<CreateCharacterResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        CreateCharacterResponse message) =>
        OnePayload(
            GameProtocol.CreateCharacterType,
            CreateCharacterPayloadCodec.Encode(message.Status));
}
