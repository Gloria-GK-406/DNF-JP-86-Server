using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.GuildRedPackets;

public sealed record GameGuildRedPacketCommand(NetworkDispatchContext Dispatch, GuildRedPacketRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildRedPacketCommandHandler(IGuildRedPacketDomainService packets, IGuildMembershipQueryDomainService guilds,
    IGuildContributionDomainService contributions,
    ICharacterInventoryEntryDomainService inventory, ISelectedSessionDomainService sessions, ICommandWorkUnits units,
    IGameCommandOutbox outbox, ICommandProjections projections, TimeProvider clock)
    : ICommandHandler<GameGuildRedPacketCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildRedPacketCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var type = (ServerMessageType)request.MessageType;
        if (!request.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildRedPacketReply(type, Error: 0x22), ct).ConfigureAwait(false);
            return false;
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        if (request.MessageType is ClientMessageType.GameGuildRedPacketList or ClientMessageType.GameGuildRedPacketPoints
            or ClientMessageType.GameGuildRedPacketHistory)
        {
            var bytes = await units.ReadOnlyAsync(context, new("guild.red-packet.read", [recipient]), token =>
                QueryAsync(actor, request, token), ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildRedPacketReply(type, Payload: bytes), ct).ConfigureAwait(false);
            return true;
        }

        var sending = request.MessageType == ClientMessageType.GameGuildRedPacketSend;
        var (result, inventoryUpdate, audience) = await units.WriteAsync(context,
            new("guild.red-packet", [recipient], Footprint: CommandFootprintMode.NonRequest),
            async (unit, token) =>
            {
                unit.Footprint.DemandActive(actor);
                var change = sending
                    ? await packets.SendAsync(actor, request.Selection, request.Shares, unit.Footprint, token).ConfigureAwait(false)
                    : await packets.TakeAsync(actor, request.Packet, token).ConfigureAwait(false);
                var update = await InventoryUpdateAsync(actor, change, token).ConfigureAwait(false);
                var members = sending && change.Guild is { } guild
                    ? await guilds.ReadMemberIdsAsync(guild, token).ConfigureAwait(false) : [];
                return (change, update, members);
            }, ct).ConfigureAwait(false);
        if (result.Status != GuildRedPacketStatus.Success)
        {
            var rejection = result.Status switch
            {
                GuildRedPacketStatus.Full => new GuildRedPacketReply(type, GuildRedPacketWire.Mutation(4, 0, 0)),
                GuildRedPacketStatus.Gone => new GuildRedPacketReply(type, GuildRedPacketWire.Mutation(1, 0, 0)),
                _ => new GuildRedPacketReply(type, Error: 0x22),
            };
            await outbox.RegisterCmdAsync(connection.ConnectionId, rejection, ct).ConfigureAwait(false);
            if (result.Status is not (GuildRedPacketStatus.Full or GuildRedPacketStatus.Gone))
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(Notice(result.Status)), ct)
                    .ConfigureAwait(false);
            return false;
        }

        List<IGameServerMessage> messages = [new GuildRedPacketReply(type,
            GuildRedPacketWire.Mutation(0, result.ItemId, result.Amount))];
        if (inventoryUpdate is not null) messages.Add(inventoryUpdate);
        await outbox.RegisterBatchAsync(connection, messages, ct).ConfigureAwait(false);
        if (sending)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(audience),
                new GuildRedPacketAvailabilityChanged(), ct).ConfigureAwait(false);
        else
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([actor]),
                new AccountWalletChanged(actor.AccountId), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<byte[]> QueryAsync(SelectedSessionIdentity actor, GuildRedPacketRequest request, CancellationToken ct) =>
        request.MessageType switch
        {
            ClientMessageType.GameGuildRedPacketList => GuildRedPacketWire.List(
                await packets.ReadAsync(actor, ct).ConfigureAwait(false), clock.GetUtcNow().ToUnixTimeSeconds()),
            ClientMessageType.GameGuildRedPacketPoints => GuildRedPacketWire.Points(
                await contributions.ReadLeaderboardAsync(actor.CharacterId, ct).ConfigureAwait(false)),
            _ => GuildRedPacketWire.History(await packets.ReadHistoryAsync(actor.CharacterId, request.Packet, ct).ConfigureAwait(false)),
        };

    private async ValueTask<IGameServerMessage?> InventoryUpdateAsync(SelectedSessionIdentity actor, GuildRedPacketResult result,
        CancellationToken ct)
    {
        if (result.Slot is not { } address) return null;
        var slot = A21InventoryLayout.ToGameSlot(address);
        if (result.RemainingAtSlot == 0)
            return GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, clock.GetUtcNow());
        var snapshot = await inventory.LoadAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false);
        var physical = LegacyInventoryStorageCoordinates.ToStorage(address);
        var item = snapshot.Containers.Single(container => container.ContainerType == physical.List).Items
            .Single(candidate => candidate.SlotIndex == physical.Index);
        return GameInventoryDelivery.Notification(new(physical.List, [item], clock.GetUtcNow()));
    }

    private static string Notice(GuildRedPacketStatus status) => status switch
    {
        GuildRedPacketStatus.AlreadyTaken => "已经领取过这个红包，或这是你自己发的红包。",
        GuildRedPacketStatus.DailyTakeLimit => "账号今日的公会红包领取次数已用完。",
        GuildRedPacketStatus.BalanceLimit => "欢乐代币券余额已达上限。",
        GuildRedPacketStatus.Unavailable => "请加入公会后使用公会红包。",
        _ => "请确认背包中有对应的红包，且发放数量为 5 到 30。",
    };
}
