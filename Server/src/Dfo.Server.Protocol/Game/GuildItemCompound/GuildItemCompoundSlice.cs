using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildItemCompound;

public sealed record GuildItemCompoundRequest(GameMessageMetadata Metadata, GuildCompoundInput? Input)
    : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => Metadata.Type == 0x0340
        ? ClientMessageType.GameCompoundGuardianGem : ClientMessageType.GameCompoundGuildMedal;
}

internal sealed class GuildItemCompoundDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 38) return new GuildItemCompoundRequest(metadata, null);

        // Leading stack bytes are not initialized by either A21 sender.
        var count = BinaryPrimitives.ReadInt32LittleEndian(body[14..]);
        var rarity = BinaryPrimitives.ReadInt32LittleEndian(body[18..]);
        var gem = metadata.Type == 0x0340;
        var low = gem ? 49 : 0;
        var materials = new List<BusinessSlot>(4);
        for (var offset = 22; offset < body.Length; offset += 4)
        {
            var index = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
            if (index == -1) continue;
            if (index < low || index >= low + 49) return new GuildItemCompoundRequest(metadata, null);
            materials.Add(A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.GuildMedal, index)));
        }

        return new GuildItemCompoundRequest(metadata, count is >= 2 and <= 4 && count == materials.Count
            && rarity is >= 0 and <= 6
            ? new(gem ? GuildCompoundKind.GuardianGem : GuildCompoundKind.Medal, rarity, materials)
            : null);
    }
}

internal sealed record GuildItemCompoundResponse(ushort Opcode, int ItemId = 0, byte Error = 0x04)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => Opcode == 0x0340
        ? ServerMessageType.GameCompoundGuardianGem : ServerMessageType.GameCompoundGuildMedal;
}

internal sealed class GuildItemCompoundEncoder : GameCmdMessageEncoder<GuildItemCompoundResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildItemCompoundResponse message)
    {
        if (message.ItemId == 0) return OnePayload(message, GameCmdEnvelope.Refuse(message.Error));
        var success = new byte[5];
        success[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(success.AsSpan(1), message.ItemId);
        return OnePayload(message, success);
    }
}

public sealed record GameGuildItemCompoundCommand(NetworkDispatchContext Dispatch, GuildItemCompoundRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuildItemCompoundCommandHandler(ISelectedSessionDomainService sessions,
    IGuildItemCompoundDomainService compounds, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<GameGuildItemCompoundCommandHandler> logger)
    : ICommandHandler<GameGuildItemCompoundCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildItemCompoundCommand command,
        CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var message = command.Input;
        if (message.Input is not { } input || context.Request is not CommandRequestContext.ProtocolEffectSelected selected
            || !sessions.IsInWorld(selected.Recipient.Session))
            return await Refuse().ConfigureAwait(false);

        var outcome = await units.WriteResultAsync(context,
            new("guild-item-compound", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    var result = await compounds.CompoundAsync(selected.Recipient.Session, input, unit.Footprint, token)
                        .ConfigureAwait(false);
                    return new Settlement(result, null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new Settlement(null, failure);
                }
            }, ct).ConfigureAwait(false);
        var settlement = outcome switch
        {
            CommandWriteResult<Settlement>.Returned returned => returned.Value,
            CommandWriteResult<Settlement>.CommitRolledBack failed => new Settlement(null, failed.Cause),
            _ => throw new InvalidOperationException("Guild compound settlement returned no outcome."),
        };
        if (settlement.Failure is ContainerAdditionException { Reason: ContainerAdditionFailure.NoRoom })
            return await Refuse(0x3C).ConfigureAwait(false);
        if (settlement.Failure is { } failure)
            logger.LogError(failure, "Guild {Kind} compound failed", input.Kind);

        if (settlement.Receipt is not { } receipt) return await Refuse().ConfigureAwait(false);

        await outbox.RegisterBatchAsync(connection, SuccessMessages(message.Metadata.Type, receipt, clock.GetUtcNow()), ct)
            .ConfigureAwait(false);
        return true;

        async ValueTask<bool> Refuse(byte reason = 0x04)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildItemCompoundResponse(message.Metadata.Type, Error: reason), ct).ConfigureAwait(false);
            return false;
        }
    }

    internal static IReadOnlyList<IGameServerMessage> SuccessMessages(ushort opcode, GuildCompoundReceipt receipt,
        DateTimeOffset at)
    {
        var replies = new List<IGameServerMessage> { new GuildItemCompoundResponse(opcode, receipt.ItemId) };
        var updated = new List<InventoryItemState>();
        var replaced = new List<GameSlot>();
        var emptied = new List<GameSlot>();
        foreach (var change in receipt.Changes)
        {
            var slot = A21InventoryLayout.ToGameSlot(change.Slot);
            if (change.Item is { } item)
            {
                updated.Add(new(item.ItemUid, slot.Index, item.ToCore()));
                if (change.ReplacesConsumedSlot) replaced.Add(slot);
            }
            else emptied.Add(slot);
        }
        replies.AddRange(GameInventoryDelivery.Notifications(
            [new(InventoryContainerType.GuildMedal, updated, at)], replaced, emptied, at));
        return replies;
    }

    private readonly record struct Settlement(GuildCompoundReceipt? Receipt, Exception? Failure);
}

public sealed class GuildItemCompoundHandler(ICommandBus commands)
    : ClientMessageHandler<GuildItemCompoundRequest>, IProtocolCommandMessageHandler<GameGuildItemCompoundCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildItemCompoundRequest message,
        CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildItemCompoundCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildItemCompoundRegistration
{
    internal static void AddGuildItemCompoundSlice(IServiceCollection services)
    {
        const ClientMessageType gem = ClientMessageType.GameCompoundGuardianGem;
        const ClientMessageType medal = ClientMessageType.GameCompoundGuildMedal;
        services.AddGameCmdMessageDecoder<GuildItemCompoundDecoder>(gem);
        services.AddGameCmdMessageDecoder<GuildItemCompoundDecoder>(medal);
        services.AddGameCmdMessageEncoder<GuildItemCompoundResponse, GuildItemCompoundEncoder>();
        services.AddProtocolCommandHandler<GameGuildItemCompoundCommand, bool, GameGuildItemCompoundCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(gem, typeof(GuildItemCompoundRequest)),
            new ProtocolCommandMessageBinding(medal, typeof(GuildItemCompoundRequest)));
        services.RegisterProtocolMessageHandler<GuildItemCompoundHandler, GuildItemCompoundRequest, GameGuildItemCompoundCommand>(gem);
        services.RegisterProtocolMessageHandler<GuildItemCompoundHandler, GuildItemCompoundRequest, GameGuildItemCompoundCommand>(medal);
    }
}
