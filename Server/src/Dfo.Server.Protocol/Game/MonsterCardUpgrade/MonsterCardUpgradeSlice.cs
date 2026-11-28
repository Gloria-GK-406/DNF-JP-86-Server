using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MonsterCardUpgrade;

internal sealed record MonsterCardUpgradeRequest(GameMessageMetadata Metadata, short TargetSlot = -1, short MaterialSlot = -1)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMonsterCardUpgrade;
}

internal sealed class MonsterCardUpgradeDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 7 || body[0] != (byte)InventoryContainerType.Main || body[3] != 1
            || body[4] != (byte)InventoryContainerType.Main)
            return new MonsterCardUpgradeRequest(metadata);
        return new MonsterCardUpgradeRequest(metadata, BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt16LittleEndian(body[5..]));
    }
}

internal sealed record MonsterCardUpgradeReply(byte Error, MonsterCardUpgradeReceipt? Attempt = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMonsterCardUpgrade;
}

internal sealed class MonsterCardUpgradeReplyEncoder : GameCmdMessageEncoder<MonsterCardUpgradeReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MonsterCardUpgradeReply message)
    {
        if (message.Attempt is not { } attempt) return OnePayload(message, new byte[] { 0, message.Error });
        var body = new byte[7];
        body[0] = 1;
        body[1] = attempt.Succeeded ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), attempt.ItemId);
        body[6] = attempt.Rank;
        return OnePayload(message, body);
    }
}

internal sealed record GameMonsterCardUpgradeCommand(NetworkDispatchContext Dispatch, MonsterCardUpgradeRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameMonsterCardUpgradeCommandHandler(ISelectedSessionDomainService sessions,
    IMonsterCardUpgradeDomainService cards, ICharacterInventoryEntryDomainService characterInventory,
    IAccountInventoryEntryDomainService accountInventory, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameMonsterCardUpgradeCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameMonsterCardUpgradeCommand command,
        CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Input;
        if (request.TargetSlot < 0 || request.MaterialSlot < 0
            || request.Metadata is { InboundOrdinal: not null, SequenceIsFresh: false }
            || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await RejectAsync(0x13).ConfigureAwait(false);
        BusinessSlot target;
        BusinessSlot material;
        try
        {
            target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.TargetSlot));
            material = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, request.MaterialSlot));
        }
        catch (InventorySlotMappingException)
        {
            return await RejectAsync(0x13).ConfigureAwait(false);
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        var settled = await units.WriteAsync(context, new("monster-card-upgrade", [recipient]), async (_, token) =>
        {
            var result = await cards.UpgradeAsync(actor, target, material, token).ConfigureAwait(false);
            if (result.Receipt is not { } attempt) return new Settlement(result);
            var character = await characterInventory.LoadAsync(actor.AccountId, actor.CharacterId, token).ConfigureAwait(false);
            var account = await accountInventory.LoadAsync(actor.AccountId, actor.CharacterId, token).ConfigureAwait(false);
            var main = character.Containers.Single(container => container.ContainerType == InventoryContainerType.Main);
            return new Settlement(result, new(main, character, account, attempt.PreparedAt));
        }, ct).ConfigureAwait(false);

        if (settled.Result.Receipt is not { } receipt)
            return await RejectAsync(settled.Result.Rejection switch
            {
                MonsterCardUpgradeRejection.InsufficientGold => 0x0A,
                MonsterCardUpgradeRejection.InventoryFull => 0x04,
                _ => 0x13,
            }).ConfigureAwait(false);
        await outbox.RegisterExplicitAsync(connection.ConnectionId,
            new([new MonsterCardUpgradeReply(0, receipt), settled.Inventory!], []), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RejectAsync(byte error)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new MonsterCardUpgradeReply(error), ct).ConfigureAwait(false);
            return false;
        }
    }

    private sealed record Settlement(MonsterCardUpgradeResult Result, InventoryFullSnapshotMessage? Inventory = null);
}

internal sealed class MonsterCardUpgradeHandler(ICommandBus commands)
    : ClientMessageHandler<MonsterCardUpgradeRequest>, IProtocolCommandMessageHandler<GameMonsterCardUpgradeCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MonsterCardUpgradeRequest message,
        CancellationToken ct) => _ = await commands.SendAsync(new GameMonsterCardUpgradeCommand(context, message), ct).ConfigureAwait(false);
}

internal static class MonsterCardUpgradeRegistration
{
    public static void AddMonsterCardUpgradeSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameMonsterCardUpgrade;
        services.AddProtocolCommandHandler<GameMonsterCardUpgradeCommand, bool, GameMonsterCardUpgradeCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(MonsterCardUpgradeRequest)));
        services.AddGameCmdMessageDecoder<MonsterCardUpgradeDecoder>(type);
        services.RegisterProtocolMessageHandler<MonsterCardUpgradeHandler, MonsterCardUpgradeRequest, GameMonsterCardUpgradeCommand>(type);
        services.AddGameCmdMessageEncoder<MonsterCardUpgradeReply, MonsterCardUpgradeReplyEncoder>();
    }
}
