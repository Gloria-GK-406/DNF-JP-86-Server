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

public sealed record CreateCharacterCommand(AccountId AccountId, byte Job, byte[] RawName)
    : ICommand<CharacterCreationResult>, IAccountCommand;

public sealed class CreateCharacterCommandHandler(ICharacterDomainService characters)
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
    IOnlineUserQueryDomainService onlineUsers,
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
