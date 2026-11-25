using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.PvpChannelInfo;

internal static class PvpChannelInfoPayloadCodec
{
    /// <summary>The ordinary failure reason; see <see cref="PvpChannelInfoResponse"/>.</summary>
    private const byte UnavailableReason = 0;

    public static byte[] Encode(PvpChannelInfoResponse response) => response.Available
        // The fixed tail of a success: i32 0 and u8 0.
        ? GameCmdEnvelope.Accept([0, 0, 0, 0, 0])
        : GameCmdEnvelope.Refuse(UnavailableReason);
}

internal sealed class PvpChannelInfoDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PvpChannelInfoRequest(metadata, body.IsEmpty);
}

internal sealed class PvpChannelInfoResponseEncoder : GameCmdMessageEncoder<PvpChannelInfoResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpChannelInfoResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(PvpChannelInfoResponse message) => PvpChannelInfoPayloadCodec.Encode(message);
}

public sealed record GamePvpChannelInfoCommand(NetworkDispatchContext Dispatch, PvpChannelInfoRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Answers whether the requester's channel is a duel channel; every request is answered.</summary>
/// <remarks>
/// Spec RQ-001: success on a duel channel, the upstream failure elsewhere. Either answer opens the client's duel
/// channel selector from an ordinary channel: A21 handler 0x1119FB0 (b1 VA) sends success and a non-0x15 failure
/// alike to 0x26BAC60, which lists the channels, and 0x26BAB90 then decides whether any duel channel is there
/// (protocol page 0x00C3).
/// </remarks>
internal sealed class GamePvpChannelInfoCommandHandler(
    ISelectedSessionDomainService sessions,
    IChannelEntryDomainService channels,
    IGameCommandOutbox outbox,
    ILogger<GamePvpChannelInfoCommandHandler> logger) : ICommandHandler<GamePvpChannelInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePvpChannelInfoCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var available = false;
        if (!command.Input.WellFormed)
            logger.LogInformation("PVP_CHANNEL_INFO connection={Connection}: a body was sent with the empty request; answered unavailable",
                connection.ConnectionId);
        else if (sessions.FindByConnection(connection.ConnectionId) is { } requester && sessions.IsInWorld(requester))
            available = (await channels.ResolveAsync(requester.ListenerPort, ct).ConfigureAwait(false)).IsPvp;
        await outbox.RegisterCmdAsync(connection.ConnectionId, new PvpChannelInfoResponse(available), ct).ConfigureAwait(false);
        return available;
    }
}

public sealed class PvpChannelInfoHandler(ICommandBus commands) : ClientMessageHandler<PvpChannelInfoRequest>,
    IProtocolCommandMessageHandler<GamePvpChannelInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PvpChannelInfoRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePvpChannelInfoCommand(context, message), ct).ConfigureAwait(false);
}

/// <summary>What the PvpChannelInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PvpChannelInfoRegistration
{
    internal static IServiceCollection AddPvpChannelInfoSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<PvpChannelInfoDecoder>(ClientMessageType.GamePvpChannelInfo);
        services.AddGameCmdMessageEncoder<PvpChannelInfoResponse, PvpChannelInfoResponseEncoder>();
        services.AddProtocolCommandHandler<GamePvpChannelInfoCommand, bool, GamePvpChannelInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpChannelInfo, typeof(PvpChannelInfoRequest)));
        services.RegisterProtocolMessageHandler<PvpChannelInfoHandler, PvpChannelInfoRequest, GamePvpChannelInfoCommand>(
            ClientMessageType.GamePvpChannelInfo);
        return services;
    }
}
