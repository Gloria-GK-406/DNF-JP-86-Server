using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.RosterFollowUp;

public sealed record GameRosterFollowUpCommand(NetworkDispatchContext Dispatch, RosterFollowUpRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameRosterFollowUpCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    IGameCommandOutbox sender,
    ILogger<RosterFollowUpHandler> logger) : ICommandHandler<GameRosterFollowUpCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameRosterFollowUpCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Roster follow-up {ConnectionId} {Attribution}: value={Value}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.Value == RosterFollowUpDescriptor.ObservedSentinel
                ? $"0x{message.Value:X4} (sentinel)"
                : $"0x{message.Value:X4}");

        // Not optional, and not merely "what the reference server happens to do". The client clears
        // a readiness flag on the roster screen immediately before sending this request and only
        // this reply sets it back; eighteen sites on that screen skip their work while it is clear.
        // Withholding the reply leaves the screen permanently inert -- which is exactly what an A/B
        // with this line removed produced. See protocol/game/client-to-server/0x02A8.md.
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new RosterFollowUpResponse(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
