using System.Buffers.Binary;
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
using Dfo.Server.Protocol.Game.Weakness;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.RecoverStamina;

public sealed record RecoverStaminaRequest(GameMessageMetadata Metadata, ushort Mode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRecoverStamina;
}

public sealed record RecoverStaminaResponse(byte ErrorCode = 0) : IGameCmdMessage
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
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RecoverStaminaResponse message) =>
        OnePayload(0x0009, message.ErrorCode == 0 ? new byte[] { 1 } : new byte[] { 0, message.ErrorCode });
}

public sealed record RecoverStaminaCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, ushort Mode)
    : ICommand<RecoverStaminaResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record RecoverStaminaResult(byte ErrorCode, int Gold = 0, int Cost = 0);

public sealed class RecoverStaminaCommandHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IDungeonRunDomainService runs, IWeaknessDomainService weakness, ICharacterDomainService characters)
    : ICommandHandler<RecoverStaminaCommand, RecoverStaminaResult>
{
    public async ValueTask<RecoverStaminaResult> HandleAsync(CommandExecutionContext context,
        RecoverStaminaCommand command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        // Only the ordinary gold button (observed body 00 00) is implemented. A client flag
        // never grants a free-service entitlement, nor silently opts into charging for one.
        if (command.Mode != 0 || sessions.FindByConnection(session.ConnectionId) != session
            || sessions.FindByCharacter(session.CharacterId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.OnlineUserId != command.OnlineUserId || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId || runs.Find(session.CharacterId) is not null)
            return new(4);

        var state = weakness.For(session.CharacterId);
        if (state.Value == 100) return new(18);
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

public sealed record StaminaRecovered(SelectedSessionIdentity Session, int Gold, DateTimeOffset PreparedAt)
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
                ? [GameInventoryDelivery.CarriedGoldNotification(notification.Gold, notification.PreparedAt),
                    new WeaknessProjectionNotification(100), new RecoverStaminaResponse()]
                : []);
}

public sealed class RecoverStaminaHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    SelectedSessionTransitionGate transitions, ICommandBus commands, INotificationBus notifications,
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
            logger.LogWarning(exception, "Gold weakness recovery failed character={CharacterId}", session.CharacterId);
            result = new(4);
        }
        logger.LogInformation("Gold weakness recovery character={CharacterId} mode={Mode} error={Error} cost={Cost} gold={Gold}",
            session.CharacterId, message.Mode, result.ErrorCode, result.Cost, result.Gold);
        if (result.ErrorCode != 0)
            await sender.SendCmdAsync(connection, new RecoverStaminaResponse(result.ErrorCode), cancellationToken)
                .ConfigureAwait(false);
        else
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new StaminaRecovered(session, result.Gold, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
    }
}
