using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildDiscovery;
using Dfo.Server.Protocol.Game.GuildInfo;

namespace Dfo.Server.Protocol.Game.GuildAlliances;

public sealed record GameGuildAllianceCommand(NetworkDispatchContext Dispatch, GuildAllianceRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildAllianceCommandHandler(IGuildAllianceDomainService alliances,
    IGuildDiscoveryDomainService discovery, IGuildOverviewDomainService overviews, ISelectedSessionDomainService sessions,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections)
    : ICommandHandler<GameGuildAllianceCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildAllianceCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var replyType = (ServerMessageType)request.MessageType;
        if (!request.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildAllianceReply(replyType, 0x24), ct).ConfigureAwait(false);
            return false;
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        if (request.MessageType is ClientMessageType.GameGuildAllianceIncoming or ClientMessageType.GameGuildAllianceOutgoing)
        {
            var incoming = request.MessageType == ClientMessageType.GameGuildAllianceIncoming;
            var reply = await units.ReadOnlyAsync(context, new("guild-alliance-list", [recipient]), async token =>
            {
                var rows = await alliances.ReadApplicationsAsync(actor.CharacterId, incoming, token).ConfigureAwait(false);
                return rows is null ? new GuildAllianceReply(replyType, 0x24)
                    : new GuildAllianceReply(replyType, Applications: rows);
            }, ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
            return reply.Error is null;
        }

        var action = request.MessageType switch
        {
            ClientMessageType.GameGuildAllianceApply => GuildAllianceAction.Apply,
            ClientMessageType.GameGuildAllianceWithdraw => GuildAllianceAction.Withdraw,
            ClientMessageType.GameGuildAllianceApprove => GuildAllianceAction.Approve,
            ClientMessageType.GameGuildAllianceReject => GuildAllianceAction.Reject,
            ClientMessageType.GameGuildAllianceDissolve => GuildAllianceAction.Dissolve,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var (changed, selfProjection, peerProjection, peerList) = await units.WriteAsync(context,
            new("guild-alliance-change", [recipient]), async (_, token) =>
            {
                var result = await alliances.ChangeAsync(actor.CharacterId, action, request.Target, request.Name,
                    request.Message, token).ConfigureAwait(false);
                if (result.Other is not { } other)
                    return (result, (AllianceProjection?)null, (AllianceProjection?)null, (GuildAllianceReply?)null);

                var refreshAlliance = action is GuildAllianceAction.Approve or GuildAllianceAction.Dissolve;
                var self = refreshAlliance
                    ? await ReadProjectionAsync(actor.CharacterId, token).ConfigureAwait(false) : null;
                var peer = refreshAlliance
                    ? await ReadProjectionAsync(other.Leader, token).ConfigureAwait(false) : null;
                var incoming = action is GuildAllianceAction.Apply or GuildAllianceAction.Withdraw;
                var list = action == GuildAllianceAction.Dissolve ? null
                    : await alliances.ReadApplicationsAsync(other.Leader, incoming, token).ConfigureAwait(false);
                return (result, self, peer, list is null ? null : new GuildAllianceReply(incoming
                    ? ServerMessageType.GameGuildAllianceIncoming : ServerMessageType.GameGuildAllianceOutgoing, Applications: list));
            }, ct).ConfigureAwait(false);

        if (changed.Other is not { } other)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildAllianceReply(replyType, changed.Status == GuildAllianceStatus.Forbidden ? (byte)0x24 : (byte)0x22),
                ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(Describe(changed.Status)), ct)
                .ConfigureAwait(false);
            return false;
        }

        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildAllianceReply(replyType), ct).ConfigureAwait(false);
        await PublishAsync(selfProjection, ct).ConfigureAwait(false);
        await PublishAsync(peerProjection, ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([other.Leader]),
            new GuildAlliancePeerChanged(action switch
            {
                GuildAllianceAction.Apply => "收到一条新的公会同盟申请。",
                GuildAllianceAction.Withdraw => "对方已撤回公会同盟申请。",
                GuildAllianceAction.Approve => "对方已同意公会同盟申请。",
                GuildAllianceAction.Reject => "对方已拒绝公会同盟申请。",
                _ => "对方已解除公会同盟。",
            }, peerList), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<AllianceProjection> ReadProjectionAsync(CharacterId viewer, CancellationToken ct)
    {
        var panel = await GuildInfoPanel.ReadAsync(overviews, viewer, ct).ConfigureAwait(false);
        var recommendations = await discovery.ReadRecommendationsAsync(viewer, ct).ConfigureAwait(false);
        return new(panel, new(recommendations));
    }

    private async ValueTask PublishAsync(AllianceProjection? change, CancellationToken ct)
    {
        if (change is not { Panel.Guild: not null })
            return;
        var audience = new NotificationAudience.TheseCharacters(change.Panel.Members.Select(member => member.Character).ToArray());
        await projections.ProjectAsync(audience, change.Panel, ct).ConfigureAwait(false);
        await projections.ProjectAsync(audience, change.Recommendations, ct).ConfigureAwait(false);
    }

    private sealed record AllianceProjection(GuildInfoPanel Panel, GuildRecommendationsChanged Recommendations);

    private static string Describe(GuildAllianceStatus status) => status switch
    {
        GuildAllianceStatus.Forbidden => "只有公会会长可以处理公会同盟。",
        GuildAllianceStatus.InvalidTarget => "请选择另一家有效的公会。",
        GuildAllianceStatus.InvalidMessage => "同盟申请留言无效。",
        GuildAllianceStatus.AlreadyAllied => "每个公会最多维持一个同盟，请先解除现有同盟。",
        GuildAllianceStatus.PendingOrListFull => "已存在同盟申请，或同盟申请列表已满。",
        GuildAllianceStatus.ApplicationMissing => "这条同盟申请已不存在。",
        _ => "这个公会同盟已不存在。",
    };
}
