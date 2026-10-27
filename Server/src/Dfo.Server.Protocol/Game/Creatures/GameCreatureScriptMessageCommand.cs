using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record GameCreatureScriptMessageCommand(NetworkDispatchContext Dispatch, CreatureScriptMessageReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameCreatureScriptMessageCommandHandler(
    ISelectedSessionDomainService sessions, ILogger<CreatureScriptMessageReportHandler> logger) : ICommandHandler<GameCreatureScriptMessageCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameCreatureScriptMessageCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var selected = sessions.FindByConnection(context.Connection.ConnectionId);
        logger.LogDebug("Creature script report mode={Mode} target={TargetKey} character={CharacterId} " +
            "bytes={Length} selected={Selected}", message.Mode, message.TargetKey,
            message.CharacterId, message.MessageBytes.Length,
            selected is not null && selected.CharacterId.Value == message.CharacterId);
        return ValueTask.FromResult(true);
    }
}
