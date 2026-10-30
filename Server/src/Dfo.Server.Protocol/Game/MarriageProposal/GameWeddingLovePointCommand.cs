using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.DomainEvents;
namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record GameWeddingLovePointCommand(NetworkDispatchContext Dispatch, WeddingLovePointRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameWeddingLovePointCommandHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    ICommandWorkUnits units, IGameCommandOutbox outbox, IMarriageCoupleDomainService couple) : ICommandHandler<GameWeddingLovePointCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingLovePointCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext c, WeddingLovePointRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var result = new MarriageLovePointResult(-1);
        if (actor is not null && execution.Request is not CommandRequestContext.ProtocolEffectRejected && m.Slot <= ushort.MaxValue)
        {
            BusinessSlot? slot = null;
            try { slot = A21InventoryLayout.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, checked((int)m.Slot))); }
            catch (InventorySlotMappingException) { }
            if (slot is { } source)
            {
                if (execution.Request is not CommandRequestContext.ProtocolEffectSelected selected || actor != selected.Recipient.Session)
                    throw new InvalidOperationException("Love coupon requires its original selected actor.");
                var accounts = await marriage.CoupleAccountsAsync(actor, ct).ConfigureAwait(false);
                result = await units.WriteAsync(execution, new("WeddingLovePoint", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                    (unit, token) => couple.UseLovePointAsync(actor, source, accounts, unit.Footprint, token), ct).ConfigureAwait(false);
            }
        }
        var own = new List<IGameServerMessage> { new WeddingLovePointAck(result.Status) };
        var targets = new List<GameExplicitMessageTarget>();
        if (result.Status == 0 && actor is not null && result.State is { } state)
        {
            own.Add(state.Item is { } item
                ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main, [new(item.ItemUid, checked((int)m.Slot), item.ToCore(), item.AvatarDetail)], state.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, checked((int)m.Slot), state.PreparedAt));
            own.Add(new WeddingLovePointNotification(actor.CharacterId.Value, result.Points));
        }
        await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, new(own, targets), ct).ConfigureAwait(false);
    }

}
