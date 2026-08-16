using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
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

    /// <summary>
    /// Success is a lone <c>0x01</c>; every refusal is <c>0x00</c> followed by a reason code.
    /// </summary>
    /// <remarks>
    /// Codes carried over from the legacy server: 0x14 for a length the wire itself rejects, 0x18
    /// for a name already in use, 0x9F for text the rules refuse. They are the only evidence we
    /// have for what the client prints, so they are reproduced rather than renumbered.
    /// </remarks>
    public static byte[] Encode(CharacterNameStatus status) => status switch
    {
        CharacterNameStatus.Available => [0x01],
        CharacterNameStatus.AlreadyTaken => [0x00, 0x18],
        CharacterNameStatus.TooShort or CharacterNameStatus.TooLong => [0x00, 0x14],
        _ => [0x00, 0x9F],
    };
}

internal sealed class CheckCharacterNameMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CheckCharacterNamePayloadCodec.Decode(metadata, body);
}

public sealed record CheckCharacterNameCommand(AccountId AccountId, byte[] RawName)
    : ICommand<CharacterNameStatus>, IAccountCommand;

public sealed class CheckCharacterNameCommandHandler(ICharacterService characters)
    : ICommandHandler<CheckCharacterNameCommand, CharacterNameStatus>
{
    public ValueTask<CharacterNameStatus> HandleAsync(
        CommandExecutionContext context,
        CheckCharacterNameCommand command,
        CancellationToken cancellationToken) =>
        characters.CheckNameAsync(command.RawName, cancellationToken);
}

public sealed class CheckCharacterNameHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
    ICommandBus commandBus,
    ILogger<CheckCharacterNameHandler> logger)
    : ClientMessageHandler<CheckCharacterNameRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        CheckCharacterNameRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        // A body too short to hold a name never reaches the domain: there is nothing to ask about.
        // It still gets an answer, because the client is holding a naming dialog open.
        var status = message.RawName is null
            ? CharacterNameStatus.Malformed
            : await commandBus.SendAsync(
                new CheckCharacterNameCommand(accountId, message.RawName),
                cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Check character name {ConnectionId} account={AccountId}: {Status} ({NameBytes}B)",
            context.Connection.ConnectionId,
            accountId,
            status,
            message.RawName?.Length ?? 0);

        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new CheckCharacterNameResponse(status),
            cancellationToken).ConfigureAwait(false);
    }
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
