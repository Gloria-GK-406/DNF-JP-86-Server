using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.EquipmentCompound;

public sealed record EquipmentCompoundRequest(GameMessageMetadata Metadata, ushort SourceSlot,
    ushort Mode, ushort Part, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameEquipmentCompound;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}

internal sealed class EquipmentCompoundDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 6
            ? new EquipmentCompoundRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
                BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), BinaryPrimitives.ReadUInt16LittleEndian(body[4..]))
            : new EquipmentCompoundRequest(metadata, 0, 0, 0, Valid: false);
}

public sealed record EquipmentCompoundResponse(ushort? ResultSlot) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEquipmentCompound;
}

internal sealed class EquipmentCompoundEncoder : GameCmdMessageEncoder<EquipmentCompoundResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EquipmentCompoundResponse message)
    {
        if (message.ResultSlot is not { } slot) return OnePayload(message, new byte[] { 0, 0x11 });
        byte[] body = [1, 0, 0];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), slot);
        return OnePayload(message, body);
    }
}

public sealed class EquipmentCompoundHandler(ICommandBus commands)
    : ClientMessageHandler<EquipmentCompoundRequest>, IProtocolCommandMessageHandler<GameEquipmentCompoundCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EquipmentCompoundRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameEquipmentCompoundCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record EquipmentCompoundCompleted(EquipmentCompoundReceipt Receipt) : INotification<EquipmentCompoundCompleted>;

public sealed class EquipmentCompoundCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<EquipmentCompoundCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EquipmentCompoundCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session)) return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        var updates = receipt.Changes.Where(x => x.Core is not null)
            .Select(x => (Slot: A21InventoryLayout.ToGameSlot(x.Slot), Core: x.Core!))
            .GroupBy(x => x.Slot.Space)
            .Select(group => new CommittedInventoryUpdateState(group.Key,
                group.Select(x => new InventoryItemState(x.Core.ItemUid, x.Slot.Index, x.Core.ToCore())).ToArray(),
                receipt.SettledAt)).ToArray();
        var cleared = receipt.Changes.Where(x => x.Core is null).Select(x => A21InventoryLayout.ToGameSlot(x.Slot));
        GameSlot[] replaced = receipt.Source == receipt.Result ? [A21InventoryLayout.ToGameSlot(receipt.Source)] : [];
        var result = A21InventoryLayout.ToGameSlot(receipt.Result);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [.. GameInventoryDelivery.Notifications(updates, replaced, cleared, receipt.SettledAt),
                new EquipmentCompoundResponse(checked((ushort)result.Index))]);
    }
}

internal static class EquipmentCompoundRegistration
{
    internal static IServiceCollection AddEquipmentCompoundSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<EquipmentCompoundDecoder>(ClientMessageType.GameEquipmentCompound);
        services.AddGameCmdMessageEncoder<EquipmentCompoundResponse, EquipmentCompoundEncoder>();
        services.AddNotificationHandler<EquipmentCompoundCompleted, EquipmentCompoundCompletedHandler>();
        services.AddProtocolCommandHandler<GameEquipmentCompoundCommand, bool, GameEquipmentCompoundCommandHandler>(
            [CommandRegistrationExit.CommittedReplayCurrentStateBatch],
            new ProtocolCommandMessageBinding(ClientMessageType.GameEquipmentCompound, typeof(EquipmentCompoundRequest)));
        services.RegisterProtocolMessageHandler<EquipmentCompoundHandler, EquipmentCompoundRequest, GameEquipmentCompoundCommand>(
            ClientMessageType.GameEquipmentCompound);
        return services;
    }
}
