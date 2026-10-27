using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Notices;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.InformNotice;

public sealed record GameNoticeSeenCommand(NetworkDispatchContext Dispatch, INoticeSeenReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => Input switch
    {
        NoticeSeenRequest primary => new ProtocolFrameMetadata.Game(primary.Metadata),
        InformNoticeSecondReport secondary => new ProtocolFrameMetadata.Game(secondary.Metadata),
        _ => throw new InvalidOperationException("Unknown notice report shape."),
    };
}
public sealed class GameNoticeSeenCommandHandler(IOnlineUserQueryDomainService users, INoticeStateDomainService notices,
    ICommandWorkUnits units, ILogger<GameNoticeSeenCommandHandler> logger) : ICommandHandler<GameNoticeSeenCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameNoticeSeenCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user)
            || user?.AccountId is not { } accountId) return false;
        if (message.Group == NoticeGroup.Secondary && user.CurrentCharacterId is null) return false;
        // Server policy, not a claim about the original game's persistence scope.
        var owner = message.Group == NoticeGroup.Primary
            ? NoticeOwner.ForAccount(accountId)
            : NoticeOwner.ForCharacter(user.CurrentCharacterId!.Value);
        var noticeId = checked((byte)message.Value);
        // Both original fine routes were account-only, including the character-owned secondary set.
        var saved = await units.WriteAsync(execution, new("MarkNoticeSeen", []),
            (_, token) => notices.MarkSeenAsync(user.SessionIdentity, owner, message.Group, noticeId, token), ct).ConfigureAwait(false);
        logger.LogDebug("Seen notice owner={Owner} group={Group} id={NoticeId} saved={Saved}", owner, message.Group, message.Value, saved);
        // Client already inserted locally. No CMD ACK and no redundant snapshot.
        return saved;
    }
}
