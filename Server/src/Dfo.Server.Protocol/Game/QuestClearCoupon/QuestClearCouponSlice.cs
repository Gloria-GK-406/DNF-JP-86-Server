using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

/// <summary>The observed 19-byte quest-clear coupon variant of CMD 0x0204.</summary>
public sealed record QuestClearCouponRequest(GameMessageMetadata Metadata, short SourceSlot, byte Container,
    uint Reserved1, uint Action, uint Reserved2, uint Reserved3, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameQuestClearCoupon;
    public bool IsSupported => Complete && SourceSlot >= 0 && Container == 0 && Reserved1 == 0
        && Action == 77 && Reserved2 == 0 && Reserved3 == 0;
}

internal static class QuestClearCouponPayloadCodec
{
    public const int RequestLength = 19;
    public const byte SilentFailure = 2;

    public static QuestClearCouponRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var slot = body.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(body) : (short)0;
        var container = body.Length >= 3 ? body[2] : (byte)0;
        if (body.Length != RequestLength)
            return new(metadata, slot, container, 0, 0, 0, 0, false);
        return new(metadata, slot, container,
            BinaryPrimitives.ReadUInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[7..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[11..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[15..]), true);
    }

    public static byte[] Encode(QuestClearCouponResponse response)
    {
        if (!response.Succeeded)
        {
            var failure = new byte[6];
            failure[1] = SilentFailure;
            BinaryPrimitives.WriteInt16LittleEndian(failure.AsSpan(2), response.SourceSlot);
            failure[4] = response.Container;
            return failure;
        }
        var success = new byte[8];
        success[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(success.AsSpan(1), response.SourceSlot);
        success[3] = response.Container;
        BinaryPrimitives.WriteUInt32LittleEndian(success.AsSpan(4), 77);
        return success;
    }
}

internal sealed class QuestClearCouponDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        QuestClearCouponPayloadCodec.Decode(metadata, body);
}

public sealed record QuestClearCouponResponse(bool Succeeded, short SourceSlot, byte Container) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameQuestClearCoupon;
}

internal sealed class QuestClearCouponEncoder : GameCmdMessageEncoder<QuestClearCouponResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(QuestClearCouponResponse message) =>
        OnePayload(GameProtocol.QuestClearCouponType, QuestClearCouponPayloadCodec.Encode(message));
}

public sealed record QuestClearCouponCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int SourceSlot)
    : ICommand<QuestClearCouponResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class QuestClearCouponCommandHandler(IQuestClearCouponDomainService coupons, TimeProvider clock)
    : ICommandHandler<QuestClearCouponCommand, QuestClearCouponResult>
{
    public ValueTask<QuestClearCouponResult> HandleAsync(CommandExecutionContext context,
        QuestClearCouponCommand command, CancellationToken ct) =>
        coupons.UseAsync(command.Session, command.SourceSlot, clock.GetUtcNow(), ct);
}

internal static class QuestClearCouponProjection
{
    public static IReadOnlyList<IServerNetworkMessage> FromLog(
        IReadOnlyList<ClearedQuest> cleared, IReadOnlyList<CarriedQuest> carried)
    {
        var flags = new byte[30_000];
        foreach (var quest in cleared)
            if ((uint)quest.QuestId < flags.Length) flags[quest.QuestId] = quest.Mark;
        return
        [
            new QuestVisibilityMessage(flags),
            new AcceptedQuestProgressMessage([.. carried.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]),
        ];
    }
}

public sealed class QuestClearCouponHandler(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    ICharacterQuestLogDomainService questLogs,
    IQuestOfferRefreshDomainService offers,
    ICommandBus commands,
    INotificationBus notifications,
    IGameMessageSender sender,
    ILogger<QuestClearCouponHandler> logger) : ClientMessageHandler<QuestClearCouponRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        QuestClearCouponRequest message, CancellationToken ct)
    {
        if (!message.IsSupported)
        {
            await RefuseAsync(context, message, ct).ConfigureAwait(false);
            return;
        }
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        {
            await RefuseAsync(context, message, ct).ConfigureAwait(false);
            return;
        }

        QuestClearCouponResult result;
        try
        {
            result = await commands.SendAsync(new QuestClearCouponCommand(
                online.OnlineUserId, session, message.SourceSlot), ct).ConfigureAwait(false);
        }
        catch (OpeningRejectedException failure)
        {
            logger.LogInformation("Quest clear coupon rejected character={CharacterId} slot={Slot}: {Reason}",
                session.CharacterId, message.SourceSlot, failure.Reason);
            await RefuseAsync(context, message, ct).ConfigureAwait(false);
            return;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(failure, "Quest clear coupon failed character={CharacterId} slot={Slot}",
                session.CharacterId, message.SourceSlot);
            await RefuseAsync(context, message, ct).ConfigureAwait(false);
            return;
        }

        logger.LogInformation("Quest clear coupon used character={CharacterId} sourceSlot={Slot} itemId={ItemId} completedCount={Count}",
            session.CharacterId, message.SourceSlot, 10_006_007, result.CompletedQuestIds.Count);
        logger.LogDebug("Quest clear coupon completed quests character={CharacterId} questIds={QuestIds}",
            session.CharacterId, string.Join(',', result.CompletedQuestIds));

        // ACK action 77 skips the client's local decrement path. The absolute inventory
        // projection follows the ACK; the two quest-log projections do not depend on offers.
        var following = new List<IServerNetworkMessage>();
        try
        {
            var log = questLogs.LogOf(session.AccountId, session.CharacterId);
            var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
            var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
            following.AddRange(QuestClearCouponProjection.FromLog(cleared, carried));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(failure, "Quest clear coupon committed but quest log refresh failed character={CharacterId}",
                session.CharacterId);
        }
        try
        {
            var current = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
            var progression = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
            if (current is not null && progression is not null)
            {
                current = await offers.RefreshAsync(current, session.AccountId, session.CharacterId,
                    progression.Level, ct).ConfigureAwait(false);
                following.Add(new AvailableQuestListMessage(progression.Level, current.World.Content.AvailableQuests));
            }
            else logger.LogWarning("Quest clear coupon committed but offer projection unavailable character={CharacterId}",
                session.CharacterId);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(failure, "Quest clear coupon committed but offer refresh failed character={CharacterId}",
                session.CharacterId);
        }
        await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Inventory,
                [new QuestClearCouponResponse(true, message.SourceSlot, message.Container)],
                FollowingMessages: following), ct).ConfigureAwait(false);
    }

    private ValueTask RefuseAsync(NetworkDispatchContext context, QuestClearCouponRequest request,
        CancellationToken ct) => sender.SendCmdAsync(context.Connection.ConnectionId,
            new QuestClearCouponResponse(false, request.SourceSlot < 0 ? (short)0 : request.SourceSlot,
                request.Container == 0 ? request.Container : (byte)0), ct);
}
