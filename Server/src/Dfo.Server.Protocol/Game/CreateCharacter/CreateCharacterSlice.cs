using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
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
    /// Success is a lone <c>0x01</c>. Refusals reproduce the legacy codes exactly.
    /// </summary>
    /// <remarks>
    /// The legacy server was not internally consistent here: a malformed request or a full account
    /// got the bare byte <c>0x04</c>, while a rejected name got the two-byte <c>00 xx</c> form. Both
    /// shapes are reproduced as they were. Under the convention that the leading byte is a status,
    /// a bare <c>0x04</c> still reads as a non-success status, so the two forms do not conflict --
    /// but the inconsistency is the legacy author's, not a simplification of ours, and normalising
    /// it would be inventing wire behaviour we have never observed.
    /// </remarks>
    public static byte[] Encode(CharacterCreationStatus status) => status switch
    {
        CharacterCreationStatus.Created => [0x01],
        CharacterCreationStatus.NameTaken => [0x00, 0x18],
        CharacterCreationStatus.InvalidName => [0x00, 0x9F],
        _ => [0x04],
    };
}

internal sealed class CreateCharacterMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CreateCharacterPayloadCodec.Decode(metadata, body);
}

public sealed record CreateCharacterCommand(AccountId AccountId, byte Job, byte[] RawName)
    : ICommand<CharacterCreationResult>, IAccountCommand;

public sealed class CreateCharacterCommandHandler(ICharacterService characters)
    : ICommandHandler<CreateCharacterCommand, CharacterCreationResult>
{
    public ValueTask<CharacterCreationResult> HandleAsync(
        CommandExecutionContext context,
        CreateCharacterCommand command,
        CancellationToken cancellationToken) =>
        characters.CreateCharacterAsync(
            command.AccountId,
            command.Job,
            command.RawName,
            cancellationToken);
}

/// <summary>
/// Creates the character, acknowledges it, then re-sends the whole roster.
/// </summary>
/// <remarks>
/// The roster refresh is not optional garnish. The acknowledgement carries no character in it, so
/// the roster notification is the only way the client learns what was created and which slot it
/// landed in. Ordering matters: the acknowledgement first, then the roster, matching the legacy
/// sequence.
/// </remarks>
public sealed class CreateCharacterHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
    ICommandBus commandBus,
    ILogger<CreateCharacterHandler> logger)
    : ClientMessageHandler<CreateCharacterRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        CreateCharacterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var result = message.RawName is null
            ? new CharacterCreationResult(CharacterCreationStatus.MalformedRequest, null)
            : await commandBus.SendAsync(
                new CreateCharacterCommand(accountId, message.Job, message.RawName),
                cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Create character {ConnectionId} account={AccountId} job={Job}: {Status} character={CharacterId}",
            context.Connection.ConnectionId,
            accountId,
            message.Job,
            result.Status,
            result.CharacterId?.Value ?? 0);

        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new CreateCharacterResponse(result.Status),
            cancellationToken).ConfigureAwait(false);

        if (result.Status != CharacterCreationStatus.Created)
        {
            return;
        }

        var roster = await commandBus.SendAsync(
            new GetAccountCharacterRosterCommand(accountId),
            cancellationToken).ConfigureAwait(false);
        await sender.SendNotiAsync(
            context.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster, AnswersRosterQuery: false),
            cancellationToken).ConfigureAwait(false);
    }
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
