using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DarkElfRuins;

public sealed record GameRuinsPlayResultCommand(NetworkDispatchContext Dispatch, RuinsPlayResultRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameRuinsRewardCommand(NetworkDispatchContext Dispatch, RuinsRewardRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameRuinsPlayResultCommandHandler(ISelectedSessionDomainService sessions,
    IDarkElfRuinsDomainService ruins, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameRuinsPlayResultCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameRuinsPlayResultCommand command, CancellationToken ct)
    {
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !sessions.IsInWorld(session)) return false;
        var read = await units.WriteAsync(execution, new("ruins-play-result", [new SelectedSessionRecipient(session, command.Dispatch.Connection)]),
            async (_, token) => new RuinsResultRead(await ruins.ResultAsync(session, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        var reward = read.Reward;
        if (reward is null || !sessions.IsInWorld(session)) return false;
        var selection = await ruins.SelectionAsync(session.CharacterId, ct).ConfigureAwait(false);
        await outbox.RegisterExplicitAsync(session.ConnectionId,
            new(RuinsProjection.Result(reward, selection, 0x032C), []), ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed class GameRuinsRewardCommandHandler(ISelectedSessionDomainService sessions,
    IDarkElfRuinsDomainService ruins, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameRuinsRewardCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameRuinsRewardCommand command, CancellationToken ct)
    {
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !sessions.IsInWorld(session)) return false;
        if (!await units.WriteAsync(execution, new("ruins-claim-reward", [new SelectedSessionRecipient(session, command.Dispatch.Connection)]),
            (_, token) => ruins.ClaimAsync(session, token), ct).ConfigureAwait(false)) return false;
        await outbox.RegisterCmdAsync(session.ConnectionId, new RuinsAcknowledgement(0x032D), ct).ConfigureAwait(false);
        return true;
    }
}
