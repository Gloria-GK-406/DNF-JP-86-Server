using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

using Dfo.Server.Abstractions.Inventory;
namespace Dfo.Server.Protocol.Game.MarriageHousing;

public sealed record GameHousingCommand(NetworkDispatchContext Dispatch, HousingNetworkRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameHousingCommandHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    ICommandWorkUnits units, IGameCommandOutbox outbox, IMarriageHousingDomainService housing, ICommandProjections projections, TimeProvider clock) : ICommandHandler<GameHousingCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameHousingCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext context, HousingNetworkRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        HousingResult result;
        if (actor is null || execution.Request is CommandRequestContext.ProtocolEffectRejected) result = new(-4);
        else
        {
            if (execution.Request is not CommandRequestContext.ProtocolEffectSelected selected || actor != selected.Recipient.Session)
                throw new InvalidOperationException("Housing requires its original selected actor.");
            var accounts = await marriage.CoupleAccountsAsync(actor, ct).ConfigureAwait(false);
            result = await units.WriteAsync(execution, new("Housing", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    try { return await housing.ExecuteAsync(actor, message.Request, accounts, unit.Footprint, token).ConfigureAwait(false); }
                    catch (HousingRejectedException error) { unit.RequireRollback(error); return new HousingResult(error.Status); }
                }, ct).ConfigureAwait(false);
        }
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new HousingAck(message.Opcode, result.Status,
            message.Request.Action == HousingAction.UpgradeRing ? result.RingLevel : message.Request.Action == HousingAction.Storage ? checked((byte)message.Request.Count) : (byte)0), ct).ConfigureAwait(false);
        if (result.Status != 0 || actor is null) return;
        if (result.ConsumedSlot is { } slot)
        {
            var wire = new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService().ToGameSlot(slot);
            await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new([result.Remaining is { } item
                ? GameInventoryDelivery.Notification(new(wire.Space, [new(item.ItemUid, wire.Index, item.ToCore(), item.AvatarDetail)], clock.GetUtcNow()))
                : GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, clock.GetUtcNow())], []), ct).ConfigureAwait(false);
        }
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(result.Audience), new HousingChanged(actor, message.Request, result), ct).ConfigureAwait(false);
    }

}
