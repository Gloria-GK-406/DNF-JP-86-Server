using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record GameRenameCreatureCommand(NetworkDispatchContext Dispatch, RenameCreatureRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed class GameRenameCreatureCommandHandler(ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings, ICreatureNamingDomainService naming, ICommandWorkUnits units,
    IGameCommandOutbox outbox, IDungeonRunDomainService runs, ITownAreaDomainService towns,
    IIllusionTowerDomainService towers, ICommandConnectionClosures connections, Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users,
    ILogger<GameRenameCreatureCommandHandler> logger, TimeProvider clock) : ICommandHandler<GameRenameCreatureCommand, bool>
{
    private sealed record RenameReceipt(CreatureRenameReceipt? Receipt);
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameRenameCreatureCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var commandRequest = context.Request ?? throw new InvalidOperationException("Rename requires its explicit request.");
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        BusinessSlot sourceSlot;
        try
        {
            sourceSlot = slotMappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Pet, request.Slot));
        }
        catch (InventorySlotMappingException)
        {
            sourceSlot = default;
        }

        CreatureRenameReceipt? receipt = null;
        if (session is not null && commandRequest is not CommandRequestContext.ProtocolEffectRejected && sourceSlot.Space == BusinessInventorySpace.CreatureConsumable)
        {
            if (commandRequest is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
                throw new InvalidOperationException("Rename requires its originally selected actor.");
            receipt = (await units.WriteAsync(context, new("RenameCreature", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) => new RenameReceipt(await naming.RenameAsync(session, sourceSlot, request.NameBytes, unit.Footprint, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false)).Receipt;
        }

        if (receipt is null)
        {
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new RenameCreatureResponse(ErrorCode: 2), cancellationToken).ConfigureAwait(false);
            return false;
        }

        var sourceUpdate = receipt.RemainingSourceCore is { } remaining
            ? GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
                InventoryContainerType.Pet,
                [new InventoryItemState(null, request.Slot, remaining.ToCore())], clock.GetUtcNow()))
            : GameInventoryDelivery.ClearedSlotNotification(
                InventoryContainerType.Pet, request.Slot, clock.GetUtcNow());
        var owner = session!;
        if (!await SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, receipt.Creature.NameBytes),
                cancellationToken).ConfigureAwait(false))
            return false;
        await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new RenameCreatureResponse(request.Slot, request.List), sourceUpdate], []),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
    private ValueTask<bool> SendVisibleAsync(SelectedSessionIdentity owner, IGameNotiMessage notification, CancellationToken ct) =>
        CreatureVisibleRecipients.DeliverAsync(owner, notification, sessions, runs, towns, towers, outbox.RegisterNotiAsync, CloseAsync, logger, ct);
    private ValueTask CloseAsync(SelectedSessionIdentity receiver, CancellationToken ct) =>
        users.TryGetByConnection(receiver.ConnectionId, out var user) && user is not null
            ? connections.RequestCloseAsync(user.Connection, ct) : ValueTask.CompletedTask;

}
internal sealed class RenameCreatureHandler(ICommandBus commands) : ClientMessageHandler<RenameCreatureRequest>, IProtocolCommandMessageHandler<GameRenameCreatureCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, RenameCreatureRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRenameCreatureCommand(context, message), ct).ConfigureAwait(false);
}
