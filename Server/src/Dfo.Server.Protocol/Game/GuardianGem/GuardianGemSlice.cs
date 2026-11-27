using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuardianGem;

public sealed record GuardianGemRequest(GameMessageMetadata Metadata, bool Valid, int Medal = 0, int Slot = 0,
    int Gem = 0, byte Socket = 0) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUseGuardianGem;
}

internal sealed class GuardianGemDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 11)
            return new GuardianGemRequest(metadata, false);
        var slot = BinaryPrimitives.ReadInt16LittleEndian(body[4..]);
        return new GuardianGemRequest(metadata, slot is >= 49 and <= 97,
            BinaryPrimitives.ReadInt32LittleEndian(body), slot, BinaryPrimitives.ReadInt32LittleEndian(body[6..]), body[10]);
    }
}

internal sealed record GuardianGemReply(GuardianGemRejection Rejection) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUseGuardianGem;
}

internal sealed class GuardianGemEncoder : GameCmdMessageEncoder<GuardianGemReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuardianGemReply message) =>
        OnePayload(message, message.Rejection switch
        {
            GuardianGemRejection.None => GameCmdEnvelope.Accept(),
            GuardianGemRejection.InvalidGem => GameCmdEnvelope.Refuse(0x12),
            _ => GameCmdEnvelope.Refuse(0x04),
        });
}

public sealed record GameGuardianGemCommand(NetworkDispatchContext Dispatch, GuardianGemRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuardianGemCommandHandler(ISelectedSessionDomainService sessions,
    IGuardianGemDomainService gems, ICommandWorkUnits units, IGameCommandOutbox outbox, TimeProvider clock)
    : ICommandHandler<GameGuardianGemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuardianGemCommand command, CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var result = new GuardianGemResult(GuardianGemRejection.InvalidTarget);
        if (input.Valid && sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            var source = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.GuildMedal, input.Slot));
            result = await units.WriteAsync(context, new("guardian-gem", [new(actor, connection, input.Metadata)]),
                (_, token) => gems.InstallAsync(actor, source, input.Medal, input.Gem, input.Socket, token), ct)
                .ConfigureAwait(false);
        }
        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuardianGemReply(result.Rejection), ct).ConfigureAwait(false);
        if (result.Medal is not { } medal)
            return false;

        var at = clock.GetUtcNow();
        await outbox.RegisterNotiAsync(connection.ConnectionId,
            result.RemainingGem is { } remaining
                ? GameInventoryDelivery.Notification(new(InventoryContainerType.GuildMedal,
                    [new(null, input.Slot, remaining.ToCore())], at))
                : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.GuildMedal, input.Slot, at), ct)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(connection.ConnectionId, GameInventoryDelivery.Notification(
            new(InventoryContainerType.Equipment, [new(medal.ItemUid, 31, medal.ToCore())], at)), ct).ConfigureAwait(false);
        return true;
    }
}

public sealed class GuardianGemHandler(ICommandBus commands)
    : ClientMessageHandler<GuardianGemRequest>, IProtocolCommandMessageHandler<GameGuardianGemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuardianGemRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuardianGemCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuardianGemRegistration
{
    internal static void AddGuardianGemSlice(IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameUseGuardianGem;
        services.AddGameCmdMessageDecoder<GuardianGemDecoder>(type);
        services.AddGameCmdMessageEncoder<GuardianGemReply, GuardianGemEncoder>();
        services.AddProtocolCommandHandler<GameGuardianGemCommand, bool, GameGuardianGemCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(GuardianGemRequest)));
        services.RegisterProtocolMessageHandler<GuardianGemHandler, GuardianGemRequest, GameGuardianGemCommand>(type);
    }
}
