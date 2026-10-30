using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.MarriageHousing;

public sealed record GameCapsuleCommand(NetworkDispatchContext Dispatch, CapsuleRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameCapsuleCommandHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    ICommandWorkUnits units, IGameCommandOutbox outbox, IMarriageCapsuleDomainService capsules, TimeProvider clock) : ICommandHandler<GameCapsuleCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCapsuleCommand command, CancellationToken ct)
    { await RunAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext context, CapsuleRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId); MarriageCapsuleResult result;
        if (actor is null || execution.Request is CommandRequestContext.ProtocolEffectRejected) result = new(-1);
        else
        {
            if (execution.Request is not CommandRequestContext.ProtocolEffectSelected selected || actor != selected.Recipient.Session)
                throw new InvalidOperationException("Capsule requires its original selected actor.");
            var accounts = await marriage.CoupleAccountsAsync(actor, ct).ConfigureAwait(false);
            result = await units.WriteAsync(execution, new("Capsule", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    try { return await capsules.CapsuleAsync(actor, message.Action, accounts, unit.Footprint, token).ConfigureAwait(false); }
                    catch (MarriageCapsuleRejectedException error) { unit.RequireRollback(error); return new MarriageCapsuleResult(error.Status); }
                }, ct).ConfigureAwait(false);
        }
        if (result.Status == 0 && actor is not null)
        {
            foreach (var item in result.Items)
            {
                var slot = A21InventoryLayout.ToGameSlot(item.Slot);
                await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new([GameInventoryDelivery.Notification(new(slot.Space,
                    [new(item.Snapshot.ItemUid, slot.Index, item.Snapshot.ToCore(), item.Snapshot.AvatarDetail)], clock.GetUtcNow()))], []), ct).ConfigureAwait(false);
            }
        }
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new CapsuleAck(message.Action, result), ct).ConfigureAwait(false);
    }

}
