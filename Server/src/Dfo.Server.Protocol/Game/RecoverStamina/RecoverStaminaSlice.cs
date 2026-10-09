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
using Dfo.Server.Application.OnlineUsers;
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

public sealed record RecoverStaminaCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, ushort Mode)
    : ICommand<RecoverStaminaResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record RecoverStaminaResult(byte ErrorCode, int Gold = 0, int Cost = 0)
{
    public AccountEntitlementEntryState? Entitlements { get; init; }
    public IReadOnlyList<CharacterPremiumUsageSlotState> Usage { get; init; } = [];
}

public sealed class RecoverStaminaCommandHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IDungeonRunDomainService runs, IWeaknessDomainService weakness, ICharacterDomainService characters,
    IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService premiumUsage,
    IIllusionTowerDomainService? towers = null)
    : ICommandHandler<RecoverStaminaCommand, RecoverStaminaResult>
{
    public async ValueTask<RecoverStaminaResult> HandleAsync(CommandExecutionContext context,
        RecoverStaminaCommand command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        // The requested payment mode does not establish entitlement.
        if (command.Mode > 1 || sessions.FindByConnection(session.ConnectionId) != session
            || sessions.FindByCharacter(session.CharacterId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.OnlineUserId != command.OnlineUserId || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId || runs.Find(session.CharacterId) is not null
            || towers?.IsInTower(session.CharacterId) == true)
            return new(4);

        var state = weakness.For(session.CharacterId);
        if (state.Value == 100) return new(18);
        if (command.Mode == 1)
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
        // The global recovery command shares the transaction execution gate with this command.
        state.Recover();
        return new(0, gold.Value, cost);
    }
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
            && sessions.FindByCharacter(notification.Session.CharacterId) == notification.Session
                ? notification.Entitlements is { } entitlements
                    ? [new WeaknessProjectionNotification(100), new RecoverStaminaResponse(Gold: notification.Gold),
                        new PremiumServiceStateMessage(entitlements, notification.Usage ?? [], notification.PreparedAt)]
                    : [GameInventoryDelivery.CarriedGoldNotification(notification.Gold, notification.PreparedAt),
                        new WeaknessProjectionNotification(100), new RecoverStaminaResponse(Gold: notification.Gold)]
                : []);
}

public sealed class RecoverStaminaHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    SelectedSessionTransitionGate transitions, ICommandBus commands, IEventService notifications,
    IGameMessageSender sender, TimeProvider clock, ILogger<RecoverStaminaHandler> logger)
    : ClientMessageHandler<RecoverStaminaRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        RecoverStaminaRequest message, CancellationToken cancellationToken)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null) return;
        await using var transition = await transitions.AcquireAsync(session.AccountId,
            UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        if (sessions.FindByConnection(connection) != session
            || sessions.FindByCharacter(session.CharacterId) != session) return;
        RecoverStaminaResult result;
        try
        {
            result = onlineUsers.TryGetByConnection(connection, out var online) && online is not null
                ? await commands.SendAsync(new RecoverStaminaCommand(online.OnlineUserId, session, message.Mode),
                    cancellationToken).ConfigureAwait(false)
                : new(4);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Weakness recovery failed character={CharacterId}", session.CharacterId);
            result = new(4);
        }
        logger.LogInformation("Weakness recovery character={CharacterId} mode={Mode} error={Error} cost={Cost} gold={Gold}",
            session.CharacterId, message.Mode, result.ErrorCode, result.Cost, result.Gold);
        if (result.ErrorCode != 0)
            await sender.SendCmdAsync(connection, new RecoverStaminaResponse(result.ErrorCode), cancellationToken)
                .ConfigureAwait(false);
        else
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new StaminaRecovered(session, result.Gold, clock.GetUtcNow(), result.Entitlements, result.Usage), cancellationToken).ConfigureAwait(false);
    }
}
