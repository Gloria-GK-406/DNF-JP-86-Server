using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.LuckyCharacter;

public sealed record LuckyCharacterBurningRequest(GameMessageMetadata Metadata, bool Enabled) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLuckyCharacterBurning;
}

internal sealed class LuckyCharacterBurningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1 && body[0] <= 1
            ? new LuckyCharacterBurningRequest(metadata, body[0] == 1)
            : throw new InvalidDataException("A21 fatigue burning expects one byte set to 0 or 1.");
}

internal sealed record LuckyCharacterBurningResponse(byte Error) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLuckyCharacterBurning;
}

internal sealed class LuckyCharacterBurningEncoder : GameCmdMessageEncoder<LuckyCharacterBurningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LuckyCharacterBurningResponse message) =>
        OnePayload(message, message.Error == 0 ? GameCmdEnvelope.Accept() : GameCmdEnvelope.Refuse(message.Error));
}

public sealed record GameLuckyCharacterBurningCommand(NetworkDispatchContext Dispatch, LuckyCharacterBurningRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameLuckyCharacterBurningCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineCharacterRuntimeDomainService runtime, ILuckyCharacterEventQuery events, ILuckyCharacterBurning burning,
    ICharacterDomainService characters, ICharacterFatigueDomainService fatigue, IChannelEntryDomainService channels, ICommandWorkUnits units,
    IGameCommandOutbox outbox, TimeProvider clock) : ICommandHandler<GameLuckyCharacterBurningCommand, bool>
{
    private const byte TownOnly = 0x13;
    private const byte InsufficientFatigue = 0x16;
    private const byte NotEligible = 0xB3;

    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameLuckyCharacterBurningCommand command,
        CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata)) return false;
        var connection = command.Dispatch.Connection;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var session))
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new LuckyCharacterBurningResponse(TownOnly), ct)
                .ConfigureAwait(false);
            return false;
        }
        var result = await units.WriteAsync(context, new("lucky-character-burning", [new(session, connection, input.Metadata)]),
            (_, token) => SetAsync(session, input.Enabled, token), ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, new LuckyCharacterBurningResponse(result.Error), ct)
            .ConfigureAwait(false);
        if (result.FatigueState is { } state)
            await outbox.RegisterNotiAsync(connection.ConnectionId, new FatigueAccelerationStateMessage(0, state), ct)
                .ConfigureAwait(false);
        return result.Error == 0;
    }

    private async ValueTask<(byte Error, byte? FatigueState)> SetAsync(SelectedSessionIdentity session, bool enabled,
        CancellationToken ct)
    {
        if (runtime.StandingOf(session) is null) return (TownOnly, null);
        if ((await channels.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false)).IsPvp) return (NotEligible, null);
        var now = clock.GetUtcNow();
        var period = await events.ReadAtAsync(now, ct).ConfigureAwait(false);
        var identity = await characters.Get(session.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (identity is null || !period.Includes(identity.Job)) return (NotEligible, 0);
        if (enabled && (await fatigue.BalanceAtAsync(session.CharacterId, now, ct).ConfigureAwait(false)).Remaining < 2)
            return (InsufficientFatigue, null);
        if (!burning.TrySet(session, period.Revision, enabled)) return (TownOnly, null);
        return (0, enabled ? (byte)2 : (byte)1);
    }
}

public sealed class LuckyCharacterBurningHandler(ICommandBus commands)
    : ClientMessageHandler<LuckyCharacterBurningRequest>, IProtocolCommandMessageHandler<GameLuckyCharacterBurningCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LuckyCharacterBurningRequest message,
        CancellationToken ct) =>
        _ = await commands.SendAsync(new GameLuckyCharacterBurningCommand(context, message), ct).ConfigureAwait(false);
}

internal static class LuckyCharacterRegistration
{
    internal static void AddLuckyCharacterSlice(this IServiceCollection services)
    {
        const ClientMessageType type = ClientMessageType.GameLuckyCharacterBurning;
        services.AddGameCmdMessageDecoder<LuckyCharacterBurningDecoder>(type);
        services.AddGameCmdMessageEncoder<LuckyCharacterBurningResponse, LuckyCharacterBurningEncoder>();
        services.AddGameNotiMessageEncoder<LuckyCharacterListMessage, GameNotiStateSynchronizationEncoder<LuckyCharacterListMessage>>();
        services.AddProtocolCommandHandler<GameLuckyCharacterBurningCommand, bool, GameLuckyCharacterBurningCommandHandler>(
            new ProtocolCommandMessageBinding(type, typeof(LuckyCharacterBurningRequest)));
        services.RegisterProtocolMessageHandler<LuckyCharacterBurningHandler, LuckyCharacterBurningRequest, GameLuckyCharacterBurningCommand>(type);
        services.AddSingleton<LuckyCharacterRosterDelivery>();
    }
}
