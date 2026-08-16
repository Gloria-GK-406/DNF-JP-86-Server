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
    /// <summary>Layout: <c>u16 slotIndex</c>, <c>i32 nameLength</c>, name bytes.</summary>
    /// <remarks>
    /// A captured request reads <c>06 00 | 04 00 00 00 | "00a6"</c>. Taking the slot as one byte
    /// puts the length field one byte early, over <c>00 04 00 00</c>, and every deletion is refused
    /// as malformed.
    /// </remarks>
    public static DeleteCharacterRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(ushort))
        {
            return new DeleteCharacterRequest(metadata, 0, null);
        }

        var slotIndex = BinaryPrimitives.ReadUInt16LittleEndian(body);
        return CharacterNameField.TryRead(body, offset: sizeof(ushort), out var name)
            ? new DeleteCharacterRequest(metadata, slotIndex, name)
            : new DeleteCharacterRequest(metadata, slotIndex, null);
    }

    /// <summary>
    /// Always three bytes: a status <c>u8</c> then the character id as a <c>u16</c>.
    /// </summary>
    /// <remarks>
    /// Deletion inverts the convention the rest of this batch uses -- <c>0x00</c> is success here,
    /// where everywhere else it introduces a refusal. The client's handler settles why: it reads
    /// the status, branches to an error path when it is non-zero, and then **both branches read the
    /// u16** and drop that character from the roster view.
    ///
    /// So the id is not optional. The reference server answered failures with a bare code and no
    /// id, which this server copied, and the result was a client that reported the deletion,
    /// read a nonexistent id as zero, removed nothing, and left the character on screen. Failures
    /// now carry an explicit zero id so the client reads what it is going to read anyway.
    ///
    /// Codes: 0x15 the confirmation name did not match, 0x02 malformed or empty slot.
    /// </remarks>
    public static byte[] Encode(CharacterDeletionStatus status, CharacterId? characterId)
    {
        var body = new byte[3];
        body[0] = status switch
        {
            CharacterDeletionStatus.Deleted => 0x00,
            CharacterDeletionStatus.NameMismatch => 0x15,
            _ => 0x02,
        };
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
            new AccountCharacterRosterResponse(roster, AnswersRosterQuery: false),
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
