using Dfo.Server.Application.Dispatching;
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

internal sealed record GameHatchCreatureCommand(NetworkDispatchContext Dispatch, HatchCreatureRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed class GameHatchCreatureCommandHandler(ISelectedSessionDomainService sessions,
    IInventorySlotMappingRuleService slotMappings, ICreatureRuntimeDomainService creatures,
    ICommandWorkUnits units, IGameCommandOutbox outbox, TimeProvider clock) : ICommandHandler<GameHatchCreatureCommand, bool>
{
    private sealed record HatchReceipt(CreatureTransformationReceipt? Receipt);
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameHatchCreatureCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        BusinessSlot? source = null;
        if (request.List == 7 && request.Slot is >= 0 and <= 139)
        {
            try { source = slotMappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Pet, request.Slot)); }
            catch (InventorySlotMappingException) { }
        }

        CreatureTransformationReceipt? receipt = null;
        if (session is not null && source is { Space: BusinessInventorySpace.CreatureInventory } egg)
        {
            receipt = (await units.WriteAsync(context, new("HatchCreature", []),
                async (unit, token) => new HatchReceipt(await creatures.HatchAsync(session, egg, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false)).Receipt;
        }
        if (receipt is null)
        {
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new HatchCreatureResponse(false, 2),
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (!sessions.IsInWorld(session!)) return true;

        var pet = receipt.After;
        var itemUpdate = GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
            InventoryContainerType.Pet, [new InventoryItemState(null, request.Slot, receipt.Core.ToCore())], clock.GetUtcNow()));
        var detail = new CreatureListMessage([new CharacterPetRuntimeState(pet.ItemUid, pet.Stomach,
            checked((byte)pet.ModeFlag), pet.Experience, pet.Mode1Field0A, pet.Mode1Field0B,
            pet.Level, pet.NameBytes, checked((byte)pet.TailFlag))]);
        await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new HatchCreatureResponse(true), itemUpdate, detail], []),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
internal sealed class HatchCreatureHandler(ICommandBus commands) : ClientMessageHandler<HatchCreatureRequest>, IProtocolCommandMessageHandler<GameHatchCreatureCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, HatchCreatureRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameHatchCreatureCommand(context, message), ct).ConfigureAwait(false);
}
