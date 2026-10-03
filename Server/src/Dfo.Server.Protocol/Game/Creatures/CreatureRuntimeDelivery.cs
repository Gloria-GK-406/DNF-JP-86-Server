using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Creatures;

public sealed record AdvanceCreatureClockCommand(SelectedSessionIdentity Session)
    : ICommand<IReadOnlyList<CreatureRuntimeChange>>;

public sealed class AdvanceCreatureClockCommandHandler(ICreatureRuntimeDomainService creatures)
    : ICommandHandler<AdvanceCreatureClockCommand, IReadOnlyList<CreatureRuntimeChange>>
{
    public ValueTask<IReadOnlyList<CreatureRuntimeChange>> HandleAsync(CommandExecutionContext context,
        AdvanceCreatureClockCommand command, CancellationToken cancellationToken) =>
        creatures.AdvanceAsync(command.Session, cancellationToken);
}

public sealed class CreatureProjectionDelivery(ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, ITownAreaDomainService towns, IGameMessageSender sender,
    ICreatureRuntimeDomainService creatures, INetworkConnectionCloser connections,
    ILogger<CreatureProjectionDelivery> logger, IInventorySlotMappingRuleService? slotMappings = null,
    TimeProvider? clock = null, IIllusionTowerDomainService? towers = null)
{
    public async ValueTask<bool> SendVisibleAsync(SelectedSessionIdentity owner, IGameNotiMessage notification,
        CancellationToken ct)
    {
        if (sessions.FindByCharacter(owner.CharacterId) != owner) return false;
        IEnumerable<SelectedSessionIdentity> receivers;
        Guid? towerRunId = null;
        if (towers?.Find(owner) is { } tower)
        {
            towerRunId = tower.RunId;
            receivers = new[] { owner }.Concat(tower.Members.Select(member => member.Session)).Distinct();
        }
        else
        {
            var audience = runs.Find(owner.CharacterId)?.Participants.Select(x => x.CharacterId)
                ?? (towns.Find(owner.CharacterId) is { } member
                    ? towns.Find(member.Area)?.VisibleTo(owner.CharacterId).Select(x => x.CharacterId) : null) ?? [];
            receivers = new[] { owner.CharacterId }.Concat(audience).Distinct()
                .Select(sessions.FindByCharacter).OfType<SelectedSessionIdentity>();
        }
        var ownerDelivered = true;
        foreach (var receiver in receivers)
        {
            if (towerRunId is { } expectedRun
                && (sessions.FindByCharacter(owner.CharacterId) != owner
                    || towers?.Find(owner)?.RunId != expectedRun))
                break;
            if (towerRunId is not null
                && (sessions.FindByCharacter(receiver.CharacterId) != receiver
                    || towers?.Find(receiver)?.RunId != towerRunId))
                continue;
            try
            {
                await sender.SendNotiAsync(receiver.ConnectionId, notification, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                if (receiver == owner) ownerDelivered = false;
                logger.LogWarning(exception,
                    "Creature notification failed for receiver={CharacterId}; closing that connection",
                    receiver.CharacterId);
                try
                {
                    if (sessions.FindByCharacter(receiver.CharacterId) == receiver)
                        await connections.RequestCloseAsync(receiver.ConnectionId, CancellationToken.None)
                            .ConfigureAwait(false);
                }
                catch (Exception closeException)
                {
                    logger.LogWarning(closeException,
                        "Creature notification receiver close failed character={CharacterId}",
                        receiver.CharacterId);
                }
            }
        }
        return ownerDelivered;
    }

    public async ValueTask SendAsync(SelectedSessionIdentity owner,
        IReadOnlyList<CreatureRuntimeChange> changes, CancellationToken ct)
    {
        if (sessions.FindByCharacter(owner.CharacterId) != owner) return;
        foreach (var change in changes)
        {
            var pet = change.After;
            var equipped = change.Equipped && creatures.IsEquipped(owner, pet.ItemUid);
            var progressed = pet.Experience != change.Before.Experience || pet.Level != change.Before.Level;
            if (change.Transformation is not null || progressed)
            {
                await sender.SendNotiAsync(owner.ConnectionId, new CreatureListMessage([
                    new CharacterPetRuntimeState(pet.ItemUid, pet.Stomach, checked((byte)pet.ModeFlag),
                        pet.Experience, pet.Mode1Field0A, pet.Mode1Field0B, pet.Level,
                        pet.NameBytes, checked((byte)pet.TailFlag))]), ct).ConfigureAwait(false);
            }
            // If it moved after evolution, the committed move already delivered the new core
            // at its current position. Never overwrite the old worn slot with a stale receipt.
            if (equipped && change.Transformation is { } transformation)
            {
                var slot = (slotMappings ?? throw new InvalidOperationException("Creature transformations require slot mapping."))
                    .ToGameSlot(transformation.Slot);
                await sender.SendExplicitAsync(owner.ConnectionId, new GameExplicitMessageDelivery([GameInventoryDelivery.Notification(
                    new CommittedInventoryUpdateState(slot.Space,
                        [new InventoryItemState(null, slot.Index, transformation.Core.ToCore())],
                        (clock ?? TimeProvider.System).GetUtcNow()))], []), ct).ConfigureAwait(false);
                await SendVisibleAsync(owner, new CreatureEvolutionNotification(
                    checked((ushort)transformation.CreatureId), owner.WireUserId), ct).ConfigureAwait(false);
            }
            await sender.SendNotiAsync(owner.ConnectionId,
                new CreatureStateNotification(checked((uint)pet.ItemUid), checked((uint)pet.Stomach)), ct)
                .ConfigureAwait(false);
            if (equipped && progressed)
                await sender.SendNotiAsync(owner.ConnectionId, new CreatureExperienceNotification(
                    checked((byte)pet.Level), checked((byte)pet.ModeFlag), checked((uint)pet.Experience),
                    checked((byte)pet.Mode1Field0A), checked((byte)pet.Mode1Field0B)), ct).ConfigureAwait(false);
            if (!equipped || (change.Before.ExactStomach > 0) == (pet.ExactStomach > 0)) continue;
            if (pet.ExactStomach <= 0)
                await SendVisibleAsync(owner, new CreatureDeathNotification(owner.WireUserId), ct).ConfigureAwait(false);
            else
                await SendVisibleAsync(owner, new CreatureRevivalNotification(owner.WireUserId), ct).ConfigureAwait(false);
        }
    }
}

/// <summary>One timer; only creatures reaching a whole-value boundary access their durable store.</summary>
public sealed class CreatureClockService(ICreatureRuntimeDomainService creatures, ICommandBus commands,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    CreatureProjectionDelivery delivery, TimeProvider clock, INetworkConnectionCloser connections,
    ILogger<CreatureClockService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                foreach (var session in creatures.DueSessions())
                    try
                    {
                        await using var lease = await transitions.AcquireAsync(session.AccountId,
                            UserAccessMode.Exclusive, stoppingToken).ConfigureAwait(false);
                        if (sessions.FindByCharacter(session.CharacterId) != session) continue;
                        await commands.SendAsync(new AdvanceCreatureClockCommand(session), stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Creature clock failed character={CharacterId}", session.CharacterId);
                    }
                foreach (var projection in creatures.DrainProjections())
                    try
                    {
                        await using var lease = await transitions.AcquireAsync(projection.Session.AccountId,
                            UserAccessMode.Exclusive, stoppingToken).ConfigureAwait(false);
                        await delivery.SendAsync(projection.Session, projection.Changes, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Creature projection failed character={CharacterId}; closing the exact connection for authoritative reload", projection.Session.CharacterId);
                        if (sessions.FindByCharacter(projection.Session.CharacterId) == projection.Session)
                            await connections.RequestCloseAsync(projection.Session.ConnectionId, stoppingToken).ConfigureAwait(false);
                    }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
