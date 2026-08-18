using System.Text;
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

public sealed record CheckCharacterNameCommand(AccountId AccountId, byte[] RawName)
    : ICommand<CharacterNameStatus>, IAccountCommand;

public sealed class CheckCharacterNameCommandHandler(ICharacterDomainService characters)
    : ICommandHandler<CheckCharacterNameCommand, CharacterNameStatus>
{
    public ValueTask<CharacterNameStatus> HandleAsync(
        CommandExecutionContext context,
        CheckCharacterNameCommand command,
        CancellationToken cancellationToken) =>
        characters.CheckNameAsync(command.RawName, cancellationToken);
}

public sealed class CheckCharacterNameHandler(
    IOnlineUserQueryDomainService onlineUsers,
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

        // The name is logged, not just its length. A job the client refuses to create produces a
        // name check and then nothing at all, so this line is the only record that an attempt
        // happened -- and without the name there is no way to tell which attempt it was.
        logger.LogInformation(
            "Check character name {ConnectionId} account={AccountId}: {Status} name='{Name}' ({NameBytes}B)",
            context.Connection.ConnectionId,
            accountId,
            status,
            DescribeName(message.RawName),
            message.RawName?.Length ?? 0);

        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new CheckCharacterNameResponse(status),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders a proposed name for the log, falling back to hex when it is not text.</summary>
    /// <remarks>
    /// Undecodable bytes become hex rather than replacement characters, so a malformed name stays
    /// distinguishable from a name that genuinely contains U+FFFD.
    /// </remarks>
    private static string DescribeName(byte[]? rawName)
    {
        if (rawName is null || rawName.Length == 0)
        {
            return "<none>";
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(rawName);
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToHexString(rawName);
        }
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
