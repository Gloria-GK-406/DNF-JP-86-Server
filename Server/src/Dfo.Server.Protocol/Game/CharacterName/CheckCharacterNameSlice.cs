using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterName;

internal static class CheckCharacterNamePayloadCodec
{
    /// <summary>Body is the bare name field: <c>i32 length</c> then that many bytes.</summary>
    public static CheckCharacterNameRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body) =>
        new(metadata, CharacterNameField.TryRead(body, offset: 0, out var name) ? name : null);

    /// <summary>The <see cref="GameCmdEnvelope"/> and nothing else.</summary>
    /// <remarks>
    /// Codes carried over from the legacy server: 0x14 for a length the wire itself rejects, 0x18
    /// for a name already in use, 0x9F for text the rules refuse. They are the only evidence we
    /// have for what the client prints, so they are reproduced rather than renumbered.
    ///
    /// This is the one reply in the family the legacy server shaped correctly, and only because its
    /// per-message status convention happened to coincide with the envelope's.
    /// </remarks>
    public static byte[] Encode(CharacterNameStatus status) => status switch
    {
        CharacterNameStatus.Available => GameCmdEnvelope.Accept(),
        CharacterNameStatus.AlreadyTaken => GameCmdEnvelope.Refuse(0x18),
        CharacterNameStatus.TooShort or CharacterNameStatus.TooLong => GameCmdEnvelope.Refuse(0x14),
        _ => GameCmdEnvelope.Refuse(0x9F),
    };
}

internal sealed class CheckCharacterNameMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CheckCharacterNamePayloadCodec.Decode(metadata, body);
}

public sealed class CheckCharacterNameHandler(ICommandBus commands)
    : ClientMessageHandler<CheckCharacterNameRequest>, IProtocolCommandMessageHandler<GameCheckCharacterNameCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CheckCharacterNameRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameCheckCharacterNameCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class CheckCharacterNameResponseEncoder
    : GameCmdMessageEncoder<CheckCharacterNameResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        CheckCharacterNameResponse message) =>
        OnePayload(
            GameProtocol.CheckCharacterNameType,
            CheckCharacterNamePayloadCodec.Encode(message.Status));
}
