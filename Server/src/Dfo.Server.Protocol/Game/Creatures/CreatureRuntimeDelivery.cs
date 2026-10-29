using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Creatures;

public sealed record AdvanceCreatureTickCommand : ICommand<bool>;
public sealed class AdvanceCreatureTickCommandHandler(ICreatureRuntimeDomainService creatures,
    ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, ITownAreaDomainService towns,
    IDungeonParticipationDomainService participation, IBossTowerDomainService bossTowers,
    IIllusionTowerDomainService towers, IGameCommandOutbox outbox, ICommandConnectionClosures closures,
    IInventorySlotMappingRuleService slotMappings, ICommandWorkUnits units, TimeProvider clock,
    ILogger<AdvanceCreatureTickCommandHandler> logger) : ICommandHandler<AdvanceCreatureTickCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, AdvanceCreatureTickCommand command, CancellationToken token)
    {
        foreach (var session in creatures.DueSessions())
        {
            if (!sessions.IsInWorld(session)) continue;
            Exception? rejected = null;
            var write = await units.WriteResultAsync(context, new("creature.clock", []), async (unit, ct) =>
            {
                try { await creatures.AdvanceAsync(session, ct).ConfigureAwait(false); return true; }
                catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
                { rejected = error; unit.RequireRollback(error); return false; }
            }, token).ConfigureAwait(false);
            if (write is CommandWriteResult<bool>.CommitRolledBack rolledBack) rejected = rolledBack.Cause;
            if (rejected is not null) logger.LogWarning(rejected, "Creature clock failed character={CharacterId}", session.CharacterId);
        }
        foreach (var projection in creatures.DrainProjections())
        {
            var phase = new ProjectionPhase();
            try { await SendCoreAsync(phase, projection.Session, projection.Changes, token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error))
            {
                if (phase.Registering) throw;
                context.Request!.DemandCurrent();
                logger.LogWarning(error, "Creature projection failed character={CharacterId}; closing the exact connection for authoritative reload", projection.Session.CharacterId);
                if (sessions.IsInWorld(projection.Session))
                    await closures.ResolveAndRequestCloseAsync(projection.Session.ConnectionId, token).ConfigureAwait(false);
            }
        }
        return true;
    }
    private sealed class ProjectionPhase { public bool Registering; }
    public ValueTask<bool> SendVisibleAsync(SelectedSessionIdentity owner, IGameNotiMessage notification, CancellationToken ct) =>
        VisibleAsync(new(), owner, notification, ct);
    private async ValueTask<bool> VisibleAsync(ProjectionPhase phase, SelectedSessionIdentity owner, IGameNotiMessage notification, CancellationToken ct)
    {
        if (!sessions.IsInWorld(owner)) return false;
        var (receivers, seated) = CreatureVisibleRecipients.Resolve(owner, sessions, runs, towns, participation, bossTowers, towers);
        var ownerDelivered = true;
        foreach (var receiver in receivers)
        {
            if (seated is not null && (!sessions.IsInWorld(owner) || !seated(owner)))
                break;
            if (seated is not null && (!sessions.IsInWorld(receiver) || !seated(receiver)))
                continue;
            try
            {
                await NotiAsync(phase, receiver.ConnectionId, notification, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                if (receiver == owner) ownerDelivered = false;
                logger.LogWarning(exception,
                    "Creature notification failed for receiver={CharacterId}; closing that connection",
                    receiver.CharacterId);
                if (sessions.IsInWorld(receiver))
                {
                    phase.Registering = true;
                    await closures.ResolveAndRequestCloseAsync(receiver.ConnectionId, CancellationToken.None).ConfigureAwait(false);
                    phase.Registering = false;
                }
            }
        }
        phase.Registering = false;
        return ownerDelivered;
    }
    private async ValueTask NotiAsync(ProjectionPhase phase, NetworkConnectionId id, IGameNotiMessage message, CancellationToken token)
    {
        phase.Registering = true;
        await outbox.RegisterNotiAsync(id, message, token).ConfigureAwait(false);
        phase.Registering = false;
    }
    private async ValueTask ExplicitAsync(ProjectionPhase phase, NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token)
    {
        phase.Registering = true;
        await outbox.RegisterExplicitAsync(id, delivery, token).ConfigureAwait(false);
        phase.Registering = false;
    }
    public ValueTask SendAsync(SelectedSessionIdentity owner, IReadOnlyList<CreatureRuntimeChange> changes, CancellationToken ct) =>
        SendCoreAsync(new(), owner, changes, ct);
    private async ValueTask SendCoreAsync(ProjectionPhase phase, SelectedSessionIdentity owner,
        IReadOnlyList<CreatureRuntimeChange> changes, CancellationToken ct)
    {
        if (!sessions.IsInWorld(owner)) return;
        foreach (var change in changes)
        {
            var pet = change.After;
            var equipped = change.Equipped && creatures.IsEquipped(owner, pet.ItemUid);
            var progressed = pet.Experience != change.Before.Experience || pet.Level != change.Before.Level;
            if (change.Transformation is not null || progressed)
            {
                await NotiAsync(phase, owner.ConnectionId, new CreatureListMessage([
                    new CharacterPetRuntimeState(pet.ItemUid, pet.Stomach, checked((byte)pet.ModeFlag),
                        pet.Experience, pet.Mode1Field0A, pet.Mode1Field0B, pet.Level,
                        pet.NameBytes, checked((byte)pet.TailFlag))]), ct).ConfigureAwait(false);
            }
            // If it moved after evolution, the committed move already delivered the new core
            // at its current position. Never overwrite the old worn slot with a stale receipt.
            if (equipped && change.Transformation is { } transformation)
            {
                var slot = slotMappings
                    .ToGameSlot(transformation.Slot);
                await ExplicitAsync(phase, owner.ConnectionId, new GameExplicitMessageDelivery([GameInventoryDelivery.Notification(
                    new CommittedInventoryUpdateState(slot.Space,
                        [new InventoryItemState(null, slot.Index, transformation.Core.ToCore())],
                        clock.GetUtcNow()))], []), ct).ConfigureAwait(false);
                await VisibleAsync(phase, owner, new CreatureEvolutionNotification(
                    checked((ushort)transformation.CreatureId), owner.WireUserId), ct).ConfigureAwait(false);
            }
            await NotiAsync(phase, owner.ConnectionId,
                new CreatureStateNotification(checked((uint)pet.ItemUid), checked((uint)pet.Stomach)), ct)
                .ConfigureAwait(false);
            if (equipped && progressed)
                await NotiAsync(phase, owner.ConnectionId, new CreatureExperienceNotification(
                    checked((byte)pet.Level), checked((byte)pet.ModeFlag), checked((uint)pet.Experience),
                    checked((byte)pet.Mode1Field0A), checked((byte)pet.Mode1Field0B)), ct).ConfigureAwait(false);
            if (!equipped || (change.Before.ExactStomach > 0) == (pet.ExactStomach > 0)) continue;
            if (pet.ExactStomach <= 0)
                await VisibleAsync(phase, owner, new CreatureDeathNotification(owner.WireUserId), ct).ConfigureAwait(false);
            else
                await VisibleAsync(phase, owner, new CreatureRevivalNotification(owner.WireUserId), ct).ConfigureAwait(false);
        }
    }
}
public sealed class CreatureClockService(ICommandBus commands, TimeProvider clock, ILogger<CreatureClockService> logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:CreatureClockService";
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new AdvanceCreatureTickCommand(), root, SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

internal static class CreatureVisibleRecipients
{
    /// <summary>
    /// Who sees an owner's creature: the members of the tower run it is seated in, otherwise its
    /// ordinary run or town area. A tower audience comes with the seat check every delivery repeats.
    /// </summary>
    internal static (IEnumerable<SelectedSessionIdentity> Receivers, Func<SelectedSessionIdentity, bool>? Seated) Resolve(
        SelectedSessionIdentity owner, ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        ITownAreaDomainService towns, IDungeonParticipationDomainService participation,
        IBossTowerDomainService bossTowers, IIllusionTowerDomainService towers)
    {
        switch (DungeonLocator.Locate(owner, participation, bossTowers, towers))
        {
            case DungeonPlace.InBossTower { Run: var boss }:
                return (new[] { owner }.Concat(boss.Members.Select(member => member.Session)).Distinct(),
                    session => bossTowers.Find(session)?.RunId == boss.RunId);
            case DungeonPlace.InIllusionTower { Run: var tower }:
                return (new[] { owner }.Concat(tower.Members.Select(member => member.Session)).Distinct(),
                    session => towers.Find(session)?.RunId == tower.RunId);
        }
        var audience = runs.Find(owner.CharacterId)?.Participants.Select(x => x.CharacterId)
            ?? (towns.Find(owner.CharacterId) is { } member
                ? towns.Find(member.Area)?.VisibleTo(owner.CharacterId).Select(x => x.CharacterId) : null) ?? [];
        return (new[] { owner.CharacterId }.Concat(audience).Distinct()
            .Select(id => sessions.TryGetInWorld(id, out var inWorld) ? inWorld : null).OfType<SelectedSessionIdentity>(), null);
    }
}
