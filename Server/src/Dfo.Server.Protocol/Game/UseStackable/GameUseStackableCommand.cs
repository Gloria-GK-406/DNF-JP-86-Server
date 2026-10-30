using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Abstractions.Characters;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UseStackable;

public sealed record GameUseStackableCommand(NetworkDispatchContext Dispatch, UseStackableRequest Input) : IProtocolCommand<ItemUseResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameUseStackableCommandHandler(ISelectedSessionDomainService sessions, IBusinessInventorySlotDomainService inventory,
    IItemUseEffectDomainService effects, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ILogger<GameUseStackableCommandHandler> logger, TimeProvider timeProvider)
    : ICommandHandler<GameUseStackableCommand, ItemUseResult>
{
    public async ValueTask<ItemUseResult> HandleAsync(CommandExecutionContext context, GameUseStackableCommand command, CancellationToken cancellationToken)
    {
        var request = context.Request ?? throw new InvalidOperationException("Use requires its explicit request.");
        var message = command.Input;
        var slot = A21InventoryLayout.ToBusinessSlot(message.Slot);
        var responseSlot = A21InventoryLayout.ToGameSlot(slot);
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        var selected = request as CommandRequestContext.ProtocolEffectSelected;
        ItemUseResult result;
        UseStackableResponse response;
        if (request is CommandRequestContext.ProtocolEffectRejected)
        {
            logger.LogWarning("Use stackable {ConnectionId}: no published session", command.Dispatch.Connection.ConnectionId);
            result = ItemUseResult.Refused(ItemUseStatus.NoSession);
            response = UseStackableResponse.Failure(responseSlot, message.InstanceValue, message.ItemCode, UseStackableResponse.Silent);
        }
        else
        {
            if (selected is null || session != selected.Recipient.Session)
                throw new InvalidOperationException("Use requires its initially captured selected session.");
            result = await units.WriteAsync(context, new("use-stackable-main", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                (unit, token) => UseAsync(session, slot, message.ItemCode, unit, token), cancellationToken).ConfigureAwait(false);
            if (result.Status == ItemUseStatus.Used)
            {
                logger.LogInformation("Use stackable {ConnectionId} character={CharacterId}: used item {ItemId} from slot {Slot}, {Remaining} left",
                    command.Dispatch.Connection.ConnectionId, session.CharacterId.Value, result.ItemId, responseSlot.Index, result.RemainingCount);
                response = UseStackableResponse.Success(responseSlot, message.InstanceValue, result.ItemId) with
                { CreatureChange = result.CreatureChange, InventoryState = result.InventoryState, ExperienceAward = result.ExperienceAward,
                  SkillPoints = result.SkillPoints, OpeningReceipt = result.OpeningReceipt };
            }
            else
            {
                logger.LogInformation("Use stackable {ConnectionId} character={CharacterId}: slot {Slot} refused as {Status}",
                    command.Dispatch.Connection.ConnectionId, session.CharacterId.Value, responseSlot.Index, result.Status);
                response = UseStackableResponse.Failure(responseSlot, message.InstanceValue, message.ItemCode, UseStackableHandler.ErrorCodeFor(result.Status))
                    with { InventoryState = result.InventoryState };
            }
        }
        List<IGameServerMessage> messages = [response];
        if (response.OpeningReceipt is { } receipt)
            foreach (var update in receipt.Updates.Where(x => x.Slot.Space == BusinessInventorySpace.VirtualResource))
                messages.Add(GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new(update.Identity, update.Slot.Index, update.Core.ToCore(), update.Core.AvatarDetail)], receipt.PreparedAt)));
        if (response.InventoryState is { } state)
        {
            // Absolute state follows the ACK so a native client decrement cannot subtract twice.
            messages.Add(state.Item is { } item
                ? GameInventoryDelivery.Notification(new(response.Slot.Space,
                    [new(item.ItemUid, response.Slot.Index, item.ToCore(), item.AvatarDetail)], state.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(response.Slot.Space, response.Slot.Index, state.PreparedAt));
        }
        if (response.ExperienceAward is { } award && response.SkillPoints is { } points)
        {
            messages.Add(new ExperienceNotification(
                award.Level,
                award.NotificationTotal,
                points,
                AccountHonorLevel.Initial,
                ExperienceBonusLines.Of(award)));
        }
        else if (response.ExperienceAward is not null || response.SkillPoints is not null)
        {
            throw new InvalidOperationException("Experience item response requires both experience and skill points.");
        }
        if (selected is null)
            await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId, new(messages, []), cancellationToken).ConfigureAwait(false);
        else
        {
            var attempt = request.BeginRegistrationAttempt(CommandRegistrationExit.SettledRequiredExplicit, command.Dispatch.Connection);
            await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId, new(messages, []), attempt, cancellationToken).ConfigureAwait(false);
        }
        return result;

    }

    private async ValueTask<ItemUseResult> UseAsync(SelectedSessionIdentity session, BusinessSlot slotAddress, int itemCode,
        CommandWorkUnit unit, CancellationToken cancellationToken)
    {
        unit.Footprint.DemandActive(session);
        if (!sessions.IsInWorld(session))
            return ItemUseResult.Refused(ItemUseStatus.NoSession);

        if (!slotAddress.Space.IsCarriedItemSpace()
            && slotAddress.Space != BusinessInventorySpace.CreatureConsumable)
            return ItemUseResult.Refused(ItemUseStatus.ListNotServed);

        var slot = inventory.Find(session.CharacterId, slotAddress);
        if (slot is null)
            return ItemUseResult.Refused(ItemUseStatus.NoBoundContainer);

        var item = await slot.Container.GetAsync(slot.Slot.Index, cancellationToken)
            .ConfigureAwait(false);

        unit.Footprint.DemandActive(session);
        // The client names what it believes it is using. A slot that holds something else means the
        // two disagree about the inventory, and deleting from it anyway would resolve that
        // disagreement in favour of whichever one is wrong.
        if (item is null || itemCode > 0 && item.ItemId != itemCode)
            return ItemUseResult.Refused(ItemUseStatus.SlotDisagrees) with
            {
                InventoryState = new(item?.Snapshot, (timeProvider ?? TimeProvider.System).GetUtcNow()),
            };

        var applied = await effects.UseAsync(session, slotAddress, item, unit.Footprint, cancellationToken).ConfigureAwait(false);
        if (applied.Status != ItemUsability.Allowed)
            return ItemUseResult.Refused(StatusFor(applied.Status), item.ItemId);
        var creatureChange = applied.Applied?.Changes<PetStomachChange>().LastOrDefault()?.Change;
        unit.Footprint.DemandActive(session);
        var remaining = await slot.Container.GetAsync(slot.Slot.Index, cancellationToken).ConfigureAwait(false);
        unit.Footprint.DemandActive(session);
        return new ItemUseResult(ItemUseStatus.Used, applied.ItemId, applied.RemainingCount, creatureChange)
        {
            InventoryState = new(remaining?.Snapshot, (timeProvider ?? TimeProvider.System).GetUtcNow()),
            ExperienceAward = applied.ExperienceAward,
            SkillPoints = applied.SkillPoints,
            OpeningReceipt = applied.OpeningReceipt,
        };
    }
    /// <summary>
    /// The refusal the judgement gave, in this protocol's own terms.
    /// </summary>
    /// <remarks>
    /// Listed rather than defaulted, for the same reason the code translation is: a verdict nobody
    /// mapped would otherwise arrive as "not a usable item" and be read out to the player as
    /// 「非消耗品， 无法使用」 -- a sentence about the item, for a judgement the server never made.
    /// </remarks>
    internal static ItemUseStatus StatusFor(ItemUsability usability) => usability switch
    {
        ItemUsability.NotAUsableItem => ItemUseStatus.NotAUsableItem,
        ItemUsability.LevelTooLow => ItemUseStatus.LevelTooLow,
        ItemUsability.JobMismatch => ItemUseStatus.JobMismatch,
        ItemUsability.OnCooldown => ItemUseStatus.OnCooldown,
        ItemUsability.PlaceNotAllowed => ItemUseStatus.PlaceNotAllowed,
        ItemUsability.CharacterStateUnavailable => ItemUseStatus.CharacterStateUnavailable,
        ItemUsability.Allowed => throw new ArgumentOutOfRangeException(
            nameof(usability), usability, "An allowed use is not a refusal."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(usability), usability, "No refusal was decided for this verdict."),
    };
}
