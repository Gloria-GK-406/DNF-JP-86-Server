using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.PvpColosseum;

internal sealed record EnterColosseumCommand(NetworkDispatchContext Dispatch, EnterColosseumRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed record UpdateColosseumRewardCommand(NetworkDispatchContext Dispatch, UpdateColosseumRewardRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class ColosseumCommands(ISelectedSessionDomainService sessions, IChannelEntryDomainService channels,
    IPvpMatchmakingDomainService matchmaking, IPvpColosseumDomainService colosseum, ICommandWorkUnits units,
    IGameCommandOutbox outbox, TimeProvider clock)
    : ICommandHandler<EnterColosseumCommand, bool>, ICommandHandler<UpdateColosseumRewardCommand, bool>
{
    private sealed record EntryResult(PvpColosseumEntry? Value);
    private sealed record DeliveryResult(PvpColosseumDelivery? Value);

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, EnterColosseumCommand command, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(command.Input.Metadata)) return false;
        var connection = command.Dispatch.Connection;
        var actor = sessions.FindByConnection(connection.ConnectionId);
        PvpColosseumEntry? result = null;
        if (actor is not null && command.Input.Kind is { } kind)
        {
            var channel = await channels.ResolveAsync(actor.ListenerPort, ct).ConfigureAwait(false);
            var entered = await units.WriteAsync(execution,
                new CommandWorkUnitDeclaration("pvp.colosseum.enter", [new(actor, connection, command.Input.Metadata)]),
                async (_, token) => new EntryResult(matchmaking.CanEnterColosseum(actor)
                    ? await colosseum.EnterAsync(actor, channel, kind, token).ConfigureAwait(false) : null), ct)
                .ConfigureAwait(false);
            result = entered.Value;
            if (result is not null)
            {
                await new PvpColosseumProjection(colosseum, outbox).ProjectAsync(actor, channel, ct).ConfigureAwait(false);
                if (result.Balance is { } balance)
                    await outbox.RegisterExplicitAsync(connection.ConnectionId, new([GameInventoryDelivery.VirtualResourceNotification(
                        [new InventoryRuntimeValueState(2, 2, balance)], clock.GetUtcNow())], []), ct).ConfigureAwait(false);
            }
        }
        await outbox.RegisterCmdAsync(connection.ConnectionId, new EnterColosseumResponse(result), ct).ConfigureAwait(false);
        return result is not null;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, UpdateColosseumRewardCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (!command.Input.WellFormed || GameRequestFreshness.IsStale(command.Input.Metadata)
            || sessions.FindByConnection(connection.ConnectionId) is not { } actor)
            return false;
        var channel = await channels.ResolveAsync(actor.ListenerPort, ct).ConfigureAwait(false);
        var collected = await units.WriteAsync(execution,
            new CommandWorkUnitDeclaration("pvp.colosseum.collect", [new(actor, connection, command.Input.Metadata)]),
            async (_, token) => new DeliveryResult(matchmaking.CanEnterColosseum(actor)
                ? await colosseum.CollectAsync(actor, channel, token).ConfigureAwait(false) : null), ct)
            .ConfigureAwait(false);
        if (collected.Value is not { } result) return false;

        var preparedAt = clock.GetUtcNow();
        var inventoryFrames = result.Items.Inventory
            .GroupBy(item => A21InventoryLayout.ToGameSlot(item.Slot).Space)
            .Select(group => new InventoryDeltaMessage(group.Key, group.GroupBy(item => item.Slot)
                .Select(slot => slot.Last())
                .Select(item => new InventoryItemState(null, A21InventoryLayout.ToGameSlot(item.Slot).Index,
                    item.Snapshot.ToCore())).ToArray(), preparedAt));
        foreach (var frame in inventoryFrames)
            await outbox.RegisterNotiAsync(connection.ConnectionId, frame, ct).ConfigureAwait(false);
        if (result.VictoryEmblemBalance is { } balance)
            await outbox.RegisterExplicitAsync(connection.ConnectionId, new([GameInventoryDelivery.VirtualResourceNotification(
                [new InventoryRuntimeValueState(2, 2, balance)], clock.GetUtcNow())], []), ct).ConfigureAwait(false);
        await new PvpColosseumProjection(colosseum, outbox).ProjectAsync(actor, channel, ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed class EnterColosseumHandler(ICommandBus commands) : ClientMessageHandler<EnterColosseumRequest>,
    IProtocolCommandMessageHandler<EnterColosseumCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, EnterColosseumRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new EnterColosseumCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class UpdateColosseumRewardHandler(ICommandBus commands) : ClientMessageHandler<UpdateColosseumRewardRequest>,
    IProtocolCommandMessageHandler<UpdateColosseumRewardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, UpdateColosseumRewardRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new UpdateColosseumRewardCommand(context, message), ct).ConfigureAwait(false);
}
