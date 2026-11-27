using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildManagement;
using Dfo.Server.Protocol.Game.GuildWarehouse;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildContents;

public sealed record GameGuildBuyContentCommand(NetworkDispatchContext Dispatch, GuildBuyContentRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildBuyContentCommandHandler(
    IGuildContentPurchaseDomainService purchases, IGuildOverviewDomainService overviews,
    ISelectedSessionDomainService sessions, ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections,
    IGuildWarehouseDomainService warehouse, IGuildNameOperationSessionDomainService drafts, IGuildRenameDomainService renaming,
    ICharacterVisibleSessionDomainService visibility, TimeProvider clock, ILogger<GameGuildBuyContentCommandHandler> logger)
    : ICommandHandler<GameGuildBuyContentCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildBuyContentCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await RefuseAsync("请进入游戏后购买公会内容。").ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        logger.LogDebug("Guild content purchase requested for character {CharacterId} with product {ProductId}",
            actor.CharacterId.Value, request.ProductId);
        if (renaming.IsGoldProduct(request.ProductId))
        {
            if (drafts.CheckedRename(actor) is not { } authorization)
                return await RefuseAsync("请先以会长身份检查新的公会名称。").ConfigureAwait(false);

            var (rename, profile) = await units.WriteAsync(context, new("guild-rename-with-gold", [recipient]), async (_, token) =>
            {
                var mutation = await renaming.RenameWithGoldAsync(actor.CharacterId, authorization, request.ProductId, token)
                    .ConfigureAwait(false);
                var updated = mutation.Status == GuildMutationStatus.Success
                    ? await GuildProfileProjection.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false)
                    : null;
                return (mutation, updated);
            }, ct).ConfigureAwait(false);

            logger.LogDebug("Guild gold rename settled for character {CharacterId} with status {Status}",
                actor.CharacterId.Value, rename.Status);
            if (rename is not { Status: GuildMutationStatus.Success, RemainingGold: { } gold } || profile is null)
                return await RefuseAsync(rename.Status switch
                {
                    GuildMutationStatus.LeaderRequired => "只有公会会长可以更改公会名称。",
                    GuildMutationStatus.InsufficientGold => "角色金币不足。",
                    GuildMutationStatus.NameTaken => "该公会名称已被使用。",
                    _ => "公会改名失败，请重新检查公会名称。",
                }).ConfigureAwait(false);

            drafts.Cancel(actor);
            await outbox.RegisterBatchAsync(connection,
                [new GuildBuyContentReply(null), GameInventoryDelivery.CarriedGoldNotification(gold, clock.GetUtcNow())],
                ct).ConfigureAwait(false);
            await profile.PublishAsync(sessions, visibility, projections, ct, authorization.Name)
                .ConfigureAwait(false);
            return true;
        }

        var result = await units.WriteAsync(context, new("guild-content-purchase", [recipient]), async (_, token) =>
        {
            var purchased = await purchases.PurchaseAsync(actor.CharacterId, request.ProductId, token).ConfigureAwait(false);
            if (purchased.Status != GuildMutationStatus.Success)
                return new PurchaseResult(purchased.Status, GuildInfoPanel.Empty, null);

            var panel = await GuildInfoPanel.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false);
            var attributes = panel.Contents.FirstOrDefault(content =>
                (content.Kind is GuildContentKind.PermanentAttributes or GuildContentKind.TemporaryAttributes)
                && content.ProductId == request.ProductId);
            var warehouseView = purchased.WarehouseCapacity is not null && panel.Guild?.Id is { } guild
                ? await warehouse.ReadAsync(guild, token).ConfigureAwait(false) : null;
            return new PurchaseResult(purchased.Status, panel, attributes?.Value, warehouseView);
        }, ct).ConfigureAwait(false);

        if (result.Status != GuildMutationStatus.Success)
            return await RefuseAsync(result.Status switch
            {
                GuildMutationStatus.LeaderRequired => "只有公会会长可以购买公会内容。",
                GuildMutationStatus.InsufficientGuildPoints => "公会积分（GM）不足。",
                GuildMutationStatus.ContentAlreadyActive => "永久公会属性已经生效。",
                GuildMutationStatus.ContentDurationLimit => "公会内容已达到有效期上限。",
                GuildMutationStatus.WarehouseCapacityLimit => "购买后会超过公会仓库容量上限。",
                GuildMutationStatus.ContributionLimit => "公会组队传送已达到次数上限。",
                _ => "此公会内容尚未开放。",
            }).ConfigureAwait(false);

        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildBuyContentReply(null), ct).ConfigureAwait(false);
        var audience = new NotificationAudience.TheseCharacters(
            result.Panel.Members.Select(member => member.Character).ToArray());
        await projections.ProjectAsync(audience, result.Panel, ct).ConfigureAwait(false);
        if (result.BuffId is { } buffId)
            await projections.ProjectAsync(audience, new GuildAttributeBenefitGranted(buffId), ct).ConfigureAwait(false);
        if (result.Warehouse is { } view)
            await projections.ProjectAsync(audience, new GuildWarehouseCapacityChanged(view), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RefuseAsync(string reason)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildBuyContentReply(0x22), ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(reason), ct).ConfigureAwait(false);
            return false;
        }
    }

    private sealed record PurchaseResult(GuildMutationStatus Status, GuildInfoPanel Panel, int? BuffId, GuildWarehouseView? Warehouse = null);
}
