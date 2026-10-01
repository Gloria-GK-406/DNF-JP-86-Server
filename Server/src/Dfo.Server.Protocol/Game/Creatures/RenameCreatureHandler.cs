using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record RenameCreatureCommand(SelectedSessionIdentity Session,
    BusinessSlot SourceSlot, byte[] NameBytes) : ICommand<RenameCreatureResult>, IRequestSettlementCommand;

internal sealed record RenameCreatureResult(CreatureRenameReceipt? Receipt);

internal sealed class RenameCreatureCommandHandler(ICreatureNamingDomainService naming)
    : ICommandHandler<RenameCreatureCommand, RenameCreatureResult>
{
    public async ValueTask<RenameCreatureResult> HandleAsync(CommandExecutionContext context,
        RenameCreatureCommand command, CancellationToken cancellationToken) =>
        new(await naming.RenameAsync(command.Session, command.SourceSlot,
            command.NameBytes, cancellationToken).ConfigureAwait(false));
}

internal sealed class RenameCreatureHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings,
    ICommandBus commands,
    IGameMessageSender sender,
    CreatureProjectionDelivery delivery,
    TimeProvider clock) : ClientMessageHandler<RenameCreatureRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RenameCreatureRequest request, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
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
        if (session is not null && sourceSlot.Space == BusinessInventorySpace.CreatureConsumable)
        {
            var result = await commands.SendAsync(
                new RenameCreatureCommand(session, sourceSlot, request.NameBytes), cancellationToken)
                .ConfigureAwait(false);
            receipt = result.Receipt;
        }

        if (receipt is null)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId,
                new RenameCreatureResponse(ErrorCode: 2), cancellationToken).ConfigureAwait(false);
            return;
        }

        var sourceUpdate = receipt.RemainingSourceCore is { } remaining
            ? GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
                InventoryContainerType.Pet,
                [new InventoryItemState(null, request.Slot, remaining)], clock.GetUtcNow()))
            : GameInventoryDelivery.ClearedSlotNotification(
                InventoryContainerType.Pet, request.Slot, clock.GetUtcNow());
        var owner = session!;
        if (!await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, receipt.Creature.NameBytes),
                cancellationToken).ConfigureAwait(false))
            return;
        await sender.SendExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(
                [new RenameCreatureResponse(request.Slot, request.List), sourceUpdate], []),
            cancellationToken).ConfigureAwait(false);
    }
}
