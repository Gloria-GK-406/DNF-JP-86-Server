using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AccountRoster;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

internal static class DeleteCharacterPayloadCodec
{
    /// <summary>Layout: <c>u8 slotIndex</c>, <c>i32 nameLength</c>, name bytes.</summary>
    public static DeleteCharacterRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return new DeleteCharacterRequest(metadata, 0, null);
        }

        var slotIndex = body[0];
        return CharacterNameField.TryRead(body, offset: 1, out var name)
            ? new DeleteCharacterRequest(metadata, slotIndex, name)
            : new DeleteCharacterRequest(metadata, slotIndex, null);
    }

    /// <summary>
    /// Deletion inverts the convention the rest of this batch uses.
    /// </summary>
    /// <remarks>
    /// Success is <c>0x00</c> followed by the deleted character id as a <c>u16</c> -- a leading
    /// zero, which everywhere else in this batch means refusal. Failures are bare non-zero codes:
    /// 0x02 malformed or empty slot, 0x15 the confirmation name did not match.
    ///
    /// This is not a transcription slip. The legacy server wrote exactly this, and the leading byte
    /// is evidently a discriminator rather than a success flag: the client has to read the id that
    /// follows, so the zero is what tells it there is one. Reproduced as observed; if a real client
    /// ever contradicts it, this remark is the place the disagreement gets recorded.
    /// </remarks>
    public static byte[] Encode(CharacterDeletionStatus status, CharacterId? characterId)
    {
        if (status != CharacterDeletionStatus.Deleted)
        {
            return status == CharacterDeletionStatus.NameMismatch ? [0x15] : [0x02];
        }

        var body = new byte[3];
        body[0] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(
            body.AsSpan(1),
            unchecked((ushort)(characterId?.Value ?? 0)));
        return body;
    }
}

internal sealed class DeleteCharacterMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        DeleteCharacterPayloadCodec.Decode(metadata, body);
}

public sealed record DeleteCharacterCommand(AccountId AccountId, ushort SlotIndex, byte[] RawName)
    : ICommand<CharacterDeletionResult>, IAccountCommand;

public sealed class DeleteCharacterCommandHandler(ICharacterService characters)
    : ICommandHandler<DeleteCharacterCommand, CharacterDeletionResult>
{
    public ValueTask<CharacterDeletionResult> HandleAsync(
        CommandExecutionContext context,
        DeleteCharacterCommand command,
        CancellationToken cancellationToken) =>
        characters.DeleteCharacterAsync(
            command.AccountId,
            command.SlotIndex,
            command.RawName,
            cancellationToken);
}

public sealed class DeleteCharacterHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
    ICommandBus commandBus,
    ILogger<DeleteCharacterHandler> logger)
    : ClientMessageHandler<DeleteCharacterRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        DeleteCharacterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var result = message.RawName is null
            ? new CharacterDeletionResult(CharacterDeletionStatus.MalformedRequest, null)
            : await commandBus.SendAsync(
                new DeleteCharacterCommand(accountId, message.SlotIndex, message.RawName),
                cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Delete character {ConnectionId} account={AccountId} slot={Slot}: {Status} character={CharacterId}",
            context.Connection.ConnectionId,
            accountId,
            message.SlotIndex,
            result.Status,
            result.CharacterId?.Value ?? 0);

        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new DeleteCharacterResponse(result.Status, result.CharacterId),
            cancellationToken).ConfigureAwait(false);

        if (result.Status != CharacterDeletionStatus.Deleted)
        {
            return;
        }

        var roster = await commandBus.SendAsync(
            new GetAccountCharacterRosterCommand(accountId),
            cancellationToken).ConfigureAwait(false);
        await sender.SendNotiAsync(
            context.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class DeleteCharacterResponseEncoder
    : GameCmdMessageEncoder<DeleteCharacterResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        DeleteCharacterResponse message) =>
        OnePayload(
            GameProtocol.DeleteCharacterType,
            DeleteCharacterPayloadCodec.Encode(message.Status, message.CharacterId));
}
