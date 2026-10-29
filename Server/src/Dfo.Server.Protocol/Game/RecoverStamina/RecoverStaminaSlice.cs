using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.RecoverStamina;

public sealed record RecoverStaminaRequest(GameMessageMetadata Metadata, ushort Mode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRecoverStamina;
}

public sealed record RecoverStaminaResponse(byte ErrorCode = 0, int Gold = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRecoverStamina;
}

internal sealed class RecoverStaminaDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2) throw new InvalidDataException("RECOVER_STAMINA requires two body bytes.");
        return new RecoverStaminaRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}

internal sealed class RecoverStaminaEncoder : GameCmdMessageEncoder<RecoverStaminaResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RecoverStaminaResponse message)
    {
        if (message.ErrorCode != 0)
            return OnePayload(0x0009, new byte[] { 0, message.ErrorCode });
        // A21 reads the authoritative balance after the success byte, even for free recovery.
        var body = new byte[5];
        body[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), checked((uint)message.Gold));
        return OnePayload(0x0009, body);
    }
}

public sealed record RecoverStaminaResult(byte ErrorCode, int Gold = 0, int Cost = 0)
{
    public AccountEntitlementEntryState? Entitlements { get; init; }
    public IReadOnlyList<CharacterPremiumUsageSlotState> Usage { get; init; } = [];
}

public sealed record StaminaRecovered(SelectedSessionIdentity Session, int Gold, DateTimeOffset PreparedAt,
    AccountEntitlementEntryState? Entitlements = null, IReadOnlyList<CharacterPremiumUsageSlotState>? Usage = null)
    : INotification<StaminaRecovered>;

public sealed class StaminaRecoveredHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<StaminaRecovered>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        StaminaRecovered notification, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.CharacterId == notification.Session.CharacterId
            && recipient.ConnectionId == notification.Session.ConnectionId
            && sessions.IsInWorld(notification.Session)
                ? notification.Entitlements is { } entitlements
                    ? [new WeaknessProjectionNotification(100), new RecoverStaminaResponse(Gold: notification.Gold),
                        new PremiumServiceStateMessage(entitlements, notification.Usage ?? [], notification.PreparedAt)]
                    : [GameInventoryDelivery.CarriedGoldNotification(notification.Gold, notification.PreparedAt),
                        new WeaknessProjectionNotification(100), new RecoverStaminaResponse(Gold: notification.Gold)]
                : []);
}

public sealed record GameRecoverStaminaCommand(NetworkDispatchContext Dispatch, RecoverStaminaRequest Input)
    : IProtocolCommand<RecoverStaminaResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameRecoverStaminaCommandHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IDungeonParticipationDomainService participation, IWeaknessDomainService weakness, ICharacterDomainService characters,
    IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService premiumUsage,
    ICommandWorkUnits units, ICommandProjections notifications, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<GameRecoverStaminaCommandHandler> logger)
    : ICommandHandler<GameRecoverStaminaCommand, RecoverStaminaResult>
{
    public async ValueTask<RecoverStaminaResult> HandleAsync(CommandExecutionContext context,
        GameRecoverStaminaCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || !sessions.IsInWorld(session)) return new(4);
        RecoverStaminaResult result;
        if (!onlineUsers.TryGetByConnection(connection, out var online) || online is null)
            result = new(4);
        else
        {
            var outcome = await units.WriteResultAsync(context, new("RecoverStamina", []), async (unit, token) =>
            {
                try { return await RecoverAsync(session, online.OnlineUserId, command.Input.Mode, token).ConfigureAwait(false); }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return Failed(failure, session);
                }
            }, ct).ConfigureAwait(false);
            result = outcome switch
            {
                CommandWriteResult<RecoverStaminaResult>.Returned returned => returned.Value,
                CommandWriteResult<RecoverStaminaResult>.CommitRolledBack rollback => Failed(rollback.Cause, session),
                _ => throw new InvalidOperationException("Weakness recovery returned no action outcome."),
            };
        }
        logger.LogInformation("Weakness recovery character={CharacterId} mode={Mode} error={Error} cost={Cost} gold={Gold}",
            session.CharacterId, command.Input.Mode, result.ErrorCode, result.Cost, result.Gold);
        if (result.ErrorCode != 0)
            await outbox.RegisterCmdAsync(connection, new RecoverStaminaResponse(result.ErrorCode), ct).ConfigureAwait(false);
        else
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new StaminaRecovered(session, result.Gold, clock.GetUtcNow(), result.Entitlements, result.Usage), ct).ConfigureAwait(false);
        return result;
    }

    private RecoverStaminaResult Failed(Exception failure, SelectedSessionIdentity session)
    {
        logger.LogWarning(failure, "Weakness recovery failed character={CharacterId}", session.CharacterId);
        return new(4);
    }

    private async ValueTask<RecoverStaminaResult> RecoverAsync(SelectedSessionIdentity session,
        OnlineUserId onlineUser, ushort mode, CancellationToken cancellationToken)
    {
        // The requested payment mode does not establish entitlement.
        if (mode > 1 || !sessions.IsInWorld(session)
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online?.OnlineUserId != onlineUser || participation.Find(session.CharacterId) is not null)
            return new(4);

        var state = weakness.For(session.CharacterId);
        if (state.Value == 100) return new(18);
        if (mode == 1)
        {
            if (!await premiumUsage.TryUseFreeWeaknessRecoveryAsync(session.AccountId, session.CharacterId,
                    cancellationToken).ConfigureAwait(false)) return new(4);
            var entitlements = new AccountEntitlementEntryState(await premiums.For(session.AccountId)
                .ReadAsync(cancellationToken).ConfigureAwait(false));
            var usage = await premiumUsage.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
            var currentGold = await characters.Get(session.CharacterId).GoldAsync(cancellationToken).ConfigureAwait(false);
            state.Recover();
            return new(0, currentGold) { Entitlements = entitlements, Usage = usage };
        }
        var cost = await state.RecoveryGoldCost(cancellationToken).ConfigureAwait(false);
        var gold = await characters.Get(session.CharacterId).TrySpendGoldAsync(cost, cancellationToken)
            .ConfigureAwait(false);
        if (gold is null) return new(22);
        // Recover enlists an infallible memory commit; database failure leaves weakness intact.
        state.Recover();
        return new(0, gold.Value, cost);
    }
}

public sealed class RecoverStaminaHandler(ICommandBus commands)
    : ClientMessageHandler<RecoverStaminaRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameRecoverStaminaCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RecoverStaminaRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRecoverStaminaCommand(context, message), ct).ConfigureAwait(false);
}
