using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PrivateStore;

// Capture stage of #43. The A21 builders of CMD 0x55 (open a stall) and 0x57 (lay out its goods)
// run inside the client's VMProtect region and cannot be read statically, so the first live run
// records their bodies. Each is acknowledged so the client goes on to the next step; nothing is
// opened or taken on the server yet. Replace with the private store slice once the layouts are known.

/// <summary>CMD 0x55 or 0x57 as received, kept whole for the record.</summary>
public sealed record PrivateStoreProbeRequest(GameMessageMetadata Metadata, bool Display, byte[] Body) : IClientNetworkMessage
{
    public ClientMessageType MessageType => Display
        ? ClientMessageType.GamePrivateStoreCompleteDisplay
        : ClientMessageType.GamePrivateStoreCreate;
}

/// <summary>The bare success both handlers accept (0x55 is an ack; 0x57 reads <c>ok:u8</c>).</summary>
public sealed record PrivateStoreProbeResponse(bool Display) : IGameCmdMessage
{
    public ServerMessageType MessageType => Display
        ? ServerMessageType.GamePrivateStoreCompleteDisplay
        : ServerMessageType.GamePrivateStoreCreate;
}

internal sealed class PrivateStoreCreateProbeDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PrivateStoreProbeRequest(metadata, false, body.ToArray());
}

internal sealed class PrivateStoreDisplayProbeDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PrivateStoreProbeRequest(metadata, true, body.ToArray());
}

internal sealed class PrivateStoreProbeResponseEncoder : GameCmdMessageEncoder<PrivateStoreProbeResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PrivateStoreProbeResponse message) =>
        OnePayload(message, GameCmdEnvelope.Accept());
}

public sealed record GamePrivateStoreProbeCommand(NetworkDispatchContext Dispatch, PrivateStoreProbeRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GamePrivateStoreProbeCommandHandler(ISelectedSessionDomainService sessions, IGameCommandOutbox outbox,
    ILogger<GamePrivateStoreProbeCommandHandler> logger) : ICommandHandler<GamePrivateStoreProbeCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GamePrivateStoreProbeCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        logger.LogWarning("PrivateStore probe {Type} character={Character} length={Length} body={Body}",
            command.Input.Display ? "0x0057" : "0x0055", session?.CharacterId.Value, command.Input.Body.Length,
            Convert.ToHexString(command.Input.Body));
        await outbox.RegisterCmdAsync(connection, new PrivateStoreProbeResponse(command.Input.Display), ct).ConfigureAwait(false);
        return true;
    }
}

public sealed class PrivateStoreProbeHandler(ICommandBus commands) : ClientMessageHandler<PrivateStoreProbeRequest>,
    IProtocolCommandMessageHandler<GamePrivateStoreProbeCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PrivateStoreProbeRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePrivateStoreProbeCommand(context, message), ct).ConfigureAwait(false);
}

/// <summary>What the PrivateStore slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PrivateStoreRegistration
{
    internal static IServiceCollection AddPrivateStoreSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<PrivateStoreCreateProbeDecoder>(ClientMessageType.GamePrivateStoreCreate);
        services.AddGameCmdMessageDecoder<PrivateStoreDisplayProbeDecoder>(ClientMessageType.GamePrivateStoreCompleteDisplay);
        services.AddGameCmdMessageEncoder<PrivateStoreProbeResponse, PrivateStoreProbeResponseEncoder>();
        services.AddProtocolCommandHandler<GamePrivateStoreProbeCommand, bool, GamePrivateStoreProbeCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePrivateStoreCreate, typeof(PrivateStoreProbeRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GamePrivateStoreCompleteDisplay, typeof(PrivateStoreProbeRequest)));
        services.RegisterProtocolMessageHandler<PrivateStoreProbeHandler, PrivateStoreProbeRequest, GamePrivateStoreProbeCommand>(
            ClientMessageType.GamePrivateStoreCreate);
        services.RegisterProtocolMessageHandler<PrivateStoreProbeHandler, PrivateStoreProbeRequest, GamePrivateStoreProbeCommand>(
            ClientMessageType.GamePrivateStoreCompleteDisplay);
        return services;
    }
}
