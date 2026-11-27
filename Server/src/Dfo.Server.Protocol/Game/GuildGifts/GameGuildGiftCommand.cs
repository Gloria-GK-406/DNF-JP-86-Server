using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildContents;
using Dfo.Server.Protocol.Game.GuildInfo;

namespace Dfo.Server.Protocol.Game.GuildGifts;

public sealed record GameGuildGiftCommand(NetworkDispatchContext Dispatch, GuildGiftRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildGiftCommandHandler(IGuildGiftDomainService gifts, IGuildOverviewDomainService overviews,
    ISelectedSessionDomainService sessions,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections)
    : ICommandHandler<GameGuildGiftCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildGiftCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var type = (ServerMessageType)request.MessageType;
        if (!request.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildGiftReply(type, 0x24), ct).ConfigureAwait(false);
            return false;
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        if (request.MessageType is ClientMessageType.GameGuildGiftList or ClientMessageType.GameGuildGiftHistory)
        {
            var reply = await units.ReadOnlyAsync(context, new("guild-gift-list", [recipient]), async token =>
            {
                var view = await gifts.ReadAsync(actor.CharacterId, request.MessageType == ClientMessageType.GameGuildGiftHistory,
                    token).ConfigureAwait(false);
                return view is null ? new GuildGiftReply(type, 0x24) : new GuildGiftReply(type, View: view);
            }, ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
            return reply.Error is null;
        }

        var sending = request.MessageType == ClientMessageType.GameGuildSendGift;
        var (result, panel, ownLists, receiverLists) = await units.WriteAsync(context, new("guild-gift", [recipient]), async (_, token) =>
        {
            var change = sending
                ? await gifts.SendAsync(actor.CharacterId, request.Recipient, request.ProductId, token).ConfigureAwait(false)
                : await gifts.ClaimAsync(actor.CharacterId, request.Key, token).ConfigureAwait(false);
            if (change.Status != GuildGiftStatus.Success)
                return (change, GuildInfoPanel.Empty, (GuildGiftListsChanged?)null, (GuildGiftListsChanged?)null);

            var updated = await GuildInfoPanel.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false);
            var notice = sending && change.Gift is { } sent
                ? $"已赠送礼物，扣除 {sent.Content.Price} 公会积分（GM）。" : null;
            var own = await ReadListsAsync(actor.CharacterId, notice, token).ConfigureAwait(false);
            var receiving = change.RecipientLeader is { } leader
                ? await ReadListsAsync(leader, "收到同盟公会的礼物。", token).ConfigureAwait(false) : null;
            return (change, updated, own, receiving);
        }, ct).ConfigureAwait(false);

        byte? error = result.Status switch
        {
            GuildGiftStatus.Success => null,
            GuildGiftStatus.LeaderRequired => 0x24,
            GuildGiftStatus.AllyRequired => 0x64,
            GuildGiftStatus.UnsupportedContent or GuildGiftStatus.MissingGift => 0x11,
            GuildGiftStatus.DailyLimit => 0x1B,
            GuildGiftStatus.InsufficientPoints or GuildGiftStatus.ActivationUnavailable => 0x0A,
            _ => 0x22,
        };
        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildGiftReply(type, error), ct).ConfigureAwait(false);
        if (error is not null)
        {
            var notice = result.Status switch
            {
                GuildGiftStatus.InsufficientPoints => "公会积分（GM）不足。",
                GuildGiftStatus.ActivationUnavailable => "当前无法激活该权益，礼物仍保留。",
                _ => null,
            };
            if (notice is not null)
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(notice), ct).ConfigureAwait(false);
            return false;
        }

        var audience = new NotificationAudience.TheseCharacters(panel.Members.Select(member => member.Character).ToArray());
        await projections.ProjectAsync(audience, panel, ct).ConfigureAwait(false);
        if (!sending && result.Gift is { Content.Kind: GuildContentKind.TemporaryAttributes } gift)
            await projections.ProjectAsync(audience, new GuildAttributeBenefitGranted(gift.Content.Value), ct).ConfigureAwait(false);
        if (ownLists is not null)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([actor.CharacterId]), ownLists, ct).ConfigureAwait(false);
        if (result.RecipientLeader is { } receivingLeader && receiverLists is not null)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receivingLeader]), receiverLists, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<GuildGiftListsChanged?> ReadListsAsync(CharacterId viewer, string? notice, CancellationToken ct)
    {
        var pending = await gifts.ReadAsync(viewer, false, ct).ConfigureAwait(false);
        var history = await gifts.ReadAsync(viewer, true, ct).ConfigureAwait(false);
        return pending is not null && history is not null ? new(pending, history, notice) : null;
    }
}
