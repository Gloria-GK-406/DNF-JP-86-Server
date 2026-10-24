using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AvatarCompound;

public sealed record AvatarCompoundRequest(GameMessageMetadata Metadata, ushort MaterialSlot, ushort FirstSlot,
    int FirstItemId, ushort SecondSlot, int SecondItemId, int TargetItemId, ushort Ability,
    byte RandomTarget, byte Category, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAvatarCompound;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.AvatarCompound;
}
internal sealed class AvatarCompoundDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var valid = body.Length == 22 || body.Length == 26 && GameBodyChecksum.IsValid(body[..22], body[22..]);
        return valid ? new AvatarCompoundRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), BinaryPrimitives.ReadInt32LittleEndian(body[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[8..]), BinaryPrimitives.ReadInt32LittleEndian(body[10..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[14..]), BinaryPrimitives.ReadUInt16LittleEndian(body[18..]), body[20], body[21])
            : new AvatarCompoundRequest(metadata, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
    }
}
public sealed record AvatarCompoundResponse(AvatarCompoundReceipt? Receipt, byte Error = 0x17,
    bool Recovery = false) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAvatarCompound;
}
internal sealed class AvatarCompoundEncoder : GameCmdMessageEncoder<AvatarCompoundResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AvatarCompoundResponse message)
    {
        if (message.Receipt is null && !message.Recovery) return OnePayload(0x0063, GameCmdEnvelope.Refuse(message.Error));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var receipt = message.Receipt;
        var mapping = new InventorySlotMappingRuleService();
        writer.Write((byte)1); writer.Write(checked((byte)(receipt?.Deductions.Length ?? 0)));
        if (receipt is not null)
        {
            if (receipt.Outputs.Length is < 1 or > 2) throw new InvalidDataException("Avatar compound output count.");
            foreach (var deduction in receipt.Deductions)
            {
                var slot = mapping.ToGameSlot(deduction.Slot);
                if (slot.Space is not (InventoryContainerType.Main or InventoryContainerType.Avatar) || deduction.Count <= 0)
                    throw new InvalidDataException("Invalid compound deduction.");
                writer.Write((byte)slot.Space); writer.Write(checked((ushort)slot.Index)); writer.Write(deduction.Count);
            }
        }
        for (var i = 0; i < 2; i++)
        {
            if (receipt is null || i >= receipt.Outputs.Length)
            {
                // Missing template 0 skips both conditional extra-block readers in A21.
                writer.Write(ushort.MaxValue); writer.Write(0); writer.Write(0); writer.Write((ushort)0);
                continue;
            }
            var output = receipt.Outputs[i]; var slot = mapping.ToGameSlot(output.Slot);
            var detail = output.Item.AvatarDetail ?? throw new InvalidDataException("Missing avatar details.");
            if (slot.Space != InventoryContainerType.Avatar || detail.JewelSocket.Length != 30)
                throw new InvalidDataException("Invalid compound output.");
            writer.Write(checked((ushort)slot.Index)); writer.Write(output.Item.ItemId);
            writer.Write(AvatarLifetimeOnTheWire.RemainingSeconds(detail.ExpireDate, receipt.At));
            writer.Write((ushort)output.Item.Attribute);
            // This result record has a 30B destination; general inventory projection uses its own layout.
            writer.Write(30); writer.Write(detail.JewelSocket);
            writer.Write(4); writer.Write(detail.Color1); writer.Write(detail.Color2);
        }
        return OnePayload(0x0063, stream.ToArray());
    }
}
public sealed record AvatarCompoundCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, AvatarCompoundInput Input)
    : ICommand<AvatarCompoundResult>, IRequestSettlementCommand, IAccountCommand
{ public AccountId AccountId => Session.AccountId; }
public sealed class AvatarCompoundCommandHandler(IAvatarCompoundDomainService avatars)
    : ICommandHandler<AvatarCompoundCommand, AvatarCompoundResult>
{
    public ValueTask<AvatarCompoundResult> HandleAsync(CommandExecutionContext context, AvatarCompoundCommand command, CancellationToken ct)
        => avatars.CompoundAsync(command.Session, command.Input, context.LegacyFootprint, ct);
}
public sealed class AvatarCompoundHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IInventorySlotMappingRuleService mappings, ICommandBus commands, IEventService events,
    IGameMessageSender sender, ILogger<AvatarCompoundHandler> logger) : ClientMessageHandler<AvatarCompoundRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AvatarCompoundRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!request.Valid || session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null) { await Reject(0x17); return; }
        AvatarCompoundInput input;
        try
        {
            input = new(mappings.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot)),
                mappings.ToBusinessSlot(new(InventoryContainerType.Avatar, request.FirstSlot)), request.FirstItemId,
                mappings.ToBusinessSlot(new(InventoryContainerType.Avatar, request.SecondSlot)), request.SecondItemId,
                request.TargetItemId, request.Ability, request.RandomTarget, request.Category);
        }
        catch (InventorySlotMappingException) { await Reject(0x17); return; }
        AvatarCompoundResult result;
        try { result = await commands.SendAsync(new AvatarCompoundCommand(online.OnlineUserId, session, input), ct).ConfigureAwait(false); }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Avatar compound failed character={Character}", session.CharacterId);
            await Reject(0x17); return;
        }
        logger.LogInformation("Avatar compound character={Character} material={Material} first={First}/{FirstId} second={Second}/{SecondId} target={Target} category={Category} result={Result} outputs={Outputs} rate={Rate}",
            session.CharacterId, request.MaterialSlot, request.FirstSlot, request.FirstItemId, request.SecondSlot,
            request.SecondItemId, request.TargetItemId, request.Category, result.Rejection,
            result.Receipt?.Outputs.Length ?? 0, result.Receipt?.RareRate ?? 0);
        if (result.Receipt is { } receipt)
            await events.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new AvatarCompoundCompleted(receipt), ct).ConfigureAwait(false);
        else await Reject(result.Rejection switch
        {
            AvatarCompoundRejection.MissingMaterial => 0x16,
            AvatarCompoundRejection.UnsupportedLifetime => 0x13,
            AvatarCompoundRejection.Locked => 0xD2,
            _ => 0x17,
        });
        ValueTask Reject(byte error) => sender.SendCmdAsync(context.Connection.ConnectionId, new AvatarCompoundResponse(null, error), ct);
    }
}
public sealed record AvatarCompoundCompleted(AvatarCompoundReceipt Receipt) : INotification<AvatarCompoundCompleted>;
public sealed class AvatarCompoundCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<AvatarCompoundCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        AvatarCompoundCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == receipt.Session.CharacterId && recipient.ConnectionId == receipt.Session.ConnectionId
            && sessions.IsInWorld(receipt.Session) ? [new AvatarCompoundResponse(receipt)] : []);
    }
}
