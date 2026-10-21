using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record HatchCreatureCommand(SelectedSessionIdentity Session, BusinessSlot EggSlot)
    : ICommand<HatchCreatureResult>;

internal sealed record HatchCreatureResult(CreatureTransformationReceipt? Receipt);

internal sealed class HatchCreatureCommandHandler(ICreatureRuntimeDomainService creatures)
    : ICommandHandler<HatchCreatureCommand, HatchCreatureResult>
{
    public async ValueTask<HatchCreatureResult> HandleAsync(CommandExecutionContext context,
        HatchCreatureCommand command, CancellationToken cancellationToken) =>
        new(await creatures.HatchAsync(command.Session, command.EggSlot, cancellationToken).ConfigureAwait(false));
}

internal sealed class HatchCreatureHandler(ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings, ICommandBus commands,
    IGameMessageSender sender, TimeProvider clock) : ClientMessageHandler<HatchCreatureRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        HatchCreatureRequest request, CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        BusinessSlot? source = null;
        if (request.List == 7 && request.Slot is >= 0 and <= 139)
        {
            try { source = slotMappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Pet, request.Slot)); }
            catch (InventorySlotMappingException) { }
        }

        CreatureTransformationReceipt? receipt = null;
        if (session is not null && source is { Space: BusinessInventorySpace.CreatureInventory } egg)
        {
            var result = await commands.SendAsync(new HatchCreatureCommand(session, egg), cancellationToken)
                .ConfigureAwait(false);
            receipt = result.Receipt;
        }
        if (receipt is null)
        {
            await sender.SendCmdAsync(context.Connection.ConnectionId, new HatchCreatureResponse(false, 2),
                cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!sessions.IsInWorld(session!)) return;

        var pet = receipt.After;
        var itemUpdate = GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
            InventoryContainerType.Pet, [new InventoryItemState(null, request.Slot, receipt.Core.ToCore())], clock.GetUtcNow()));
        var detail = new CreatureListMessage([new CharacterPetRuntimeState(pet.ItemUid, pet.Stomach,
            checked((byte)pet.ModeFlag), pet.Experience, pet.Mode1Field0A, pet.Mode1Field0B,
            pet.Level, pet.NameBytes, checked((byte)pet.TailFlag))]);
        await sender.SendExplicitAsync(context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new HatchCreatureResponse(true), itemUpdate, detail], []),
            cancellationToken).ConfigureAwait(false);
    }
}
