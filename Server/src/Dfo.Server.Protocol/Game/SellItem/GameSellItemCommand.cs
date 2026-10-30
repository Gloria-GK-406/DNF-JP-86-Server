using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed record GameSellItemCommand(NetworkDispatchContext Dispatch, SellItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameSellItemCommandHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, IInventorySlotMappingRuleService slotMappings,
    IItemSaleDomainService sales, TimeProvider clock, ICommandWorkUnits units,
    ICommandProjections projections, IGameCommandOutbox outbox, ILogger<GameSellItemCommandHandler> logger)
    : ICommandHandler<GameSellItemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameSellItemCommand command, CancellationToken ct)
    {
        var message = command.Input;
        var slot = slotMappings.ToBusinessSlot(message.Slot);
        var session = sessions.TryGetInWorld(command.Dispatch.Connection.ConnectionId, out var inWorld) ? inWorld : null;
        ItemSaleResult result;
        if (message.FieldSum != (byte)message.Slot.Space + message.Slot.Index + message.Count)
            result = new(ItemSaleRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online) || online is null)
            result = new(ItemSaleRejection.NoSession);
        else
        {
            var outcome = await units.WriteAsync(context, new("SellItem", [new(session, command.Dispatch.Connection, message.Metadata)]), async (unit, token) =>
            {
                ItemSaleResult? value = null;
                ItemSaleRejectedException? rejected = null;
                try { value = await sales.SellAsync(session, slot, message.Count, message.NpcId, clock.GetUtcNow(), token).ConfigureAwait(false); }
                catch (ItemSaleRejectedException error) { rejected = error; }
                if (rejected is not null) unit.RequireRollback(rejected);
                return new Outcome(value, rejected);
            }, ct).ConfigureAwait(false);
            result = outcome.Rejected is { } rejection ? new(rejection.Reason)
                : outcome.Value ?? throw new InvalidOperationException("Sale returned no result.");
        }
        if (result.Receipt is { } receipt)
        {
            logger.LogInformation("NPC sale character={CharacterId} npc={NpcId} slot={Slot} item={ItemId} count={Count} gold={Gold}",
                receipt.Session.CharacterId.Value, message.NpcId, receipt.Slot, receipt.ItemId, receipt.Count, receipt.Gold);
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new ItemSaleCompleted(receipt), ct).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("NPC sale {ConnectionId} npc={NpcId} container={Container} slot={Slot} count={Count} refused={Reason}",
                command.Dispatch.Connection.ConnectionId, message.NpcId, message.Slot.Space, message.Slot.Index, message.Count, result.Rejection);
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new SellItemResponse(null, ErrorCode: ErrorCode(result.Rejection)), ct).ConfigureAwait(false);
        }
        return result.Receipt is not null;
    }
    private sealed record Outcome(ItemSaleResult? Value, ItemSaleRejectedException? Rejected);
    private static byte ErrorCode(ItemSaleRejection reason) => reason switch
    {
        ItemSaleRejection.Locked => 0xd6,
        ItemSaleRejection.GoldLimit => 22,
        ItemSaleRejection.ItemUnavailable => 17,
        ItemSaleRejection.NoSession or ItemSaleRejection.InvalidRequest or ItemSaleRejection.CannotSell => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
