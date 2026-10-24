using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.AuctionService;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.InformNotice;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Login;

public readonly record struct GameLoginCompleted;
public sealed record GameLoginCommand(NetworkDispatchContext Dispatch, GameLoginRequest Request)
    : IProtocolCommand<GameLoginCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameLoginCommandHandler(
    IOnlineUserDomainService onlineUsers,
    IGameLoginAuthenticationDomainService authentication,
    IAccountEntryStateDomainService accountEntryState,
    IChannelEntryDomainService channels,
    IClientSettingsDomainService clientSettings,
    ICommandWorkUnits workUnits,
    IGameCommandOutbox outbox,
    ICommandConnectionClosures closer,
    ILogger<GameLoginCommandHandler> logger) : ICommandHandler<GameLoginCommand, GameLoginCompleted>
{
    private readonly record struct Redemption(Dfo.Server.Data.Accounts.AccountId? AccountId);

    public async ValueTask<GameLoginCompleted> HandleAsync(CommandExecutionContext context,
        GameLoginCommand command, CancellationToken cancellationToken)
    {
        var connectionId = command.Dispatch.Connection.ConnectionId;
        if (!onlineUsers.TryGetByConnection(connectionId, out var onlineUser)
            || onlineUser is null)
        {
            await RejectAsync(command.Dispatch.Connection, "missing_online_registration").ConfigureAwait(false);
            return default;
        }

        var commandCompleted = false;
        try
        {
            var login = await workUnits.WriteAsync(context,
                new CommandWorkUnitDeclaration("game-login-redemption", []),
                async (_, ct) => new Redemption(await authentication.AuthenticateSessionAsync(
                    onlineUser.OnlineUserId, command.Request.MId, command.Request.Ticket, ct).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);
            commandCompleted = true;
            if (login.AccountId is not { } accountId)
            {
                await RejectAsync(command.Dispatch.Connection, "ticket_rejected").ConfigureAwait(false);
                return default;
            }

            if (!onlineUsers.TryGetByConnection(connectionId, out var authenticated)
                || authenticated is null
                || authenticated.OnlineUserId != onlineUser.OnlineUserId
                || authenticated.AccountId != accountId
                || authenticated.State != OnlineUserState.Authenticated)
            {
                await RejectAsync(command.Dispatch.Connection, "identity_not_published").ConfigureAwait(false);
                return default;
            }

            var notifications = await BuildAccountSettingsAsync(
                accountId,
                cancellationToken).ConfigureAwait(false);
            var entry = await channels.ResolveLoginAsync(
                command.Dispatch.Connection.ListenerPort, cancellationToken).ConfigureAwait(false);
            var response = new GameLoginResponse(true, entry.Environment, entry.Endpoints.ConnectionPort,
                entry.Endpoints.Host, entry.Endpoints.EndpointPortA, entry.Endpoints.EndpointPortB);
            await outbox.RegisterCmdAsync(connectionId, response, cancellationToken).ConfigureAwait(false);
            foreach (var notification in notifications)
            {
                await outbox.RegisterNotiAsync(connectionId, notification, cancellationToken)
                    .ConfigureAwait(false);
                if (notification is LoginHotkeyStateMessage)
                {
                    var origin = new Dfo.Server.Data.Accounts.ClientSettingsOrigin(authenticated.SessionIdentity, accountId, authenticated.Generation, null);
                    if (!clientSettings.ActivateHotkeyProfile(origin, 0))
                        throw new InvalidOperationException("Login hotkey profile identity changed during delivery.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            await RejectAsync(
                command.Dispatch.Connection,
                commandCompleted ? "delivery_cancelled" : "redemption_cancelled")
                .ConfigureAwait(false);
        }
        catch
        {
            await RejectAsync(
                command.Dispatch.Connection,
                commandCompleted ? "delivery_failed" : "redemption_failed")
                .ConfigureAwait(false);
        }
        return default;
    }

    /// <summary>
    /// Pushes the account-scoped state the client expects before the roster screen.
    /// </summary>
    /// <remarks>
    /// Six notifications in the reference server's exact order: game options, hotkeys, both auction
    /// service states, honour standing, then the inform notice. Sending only some of them is what
    /// this server did for a long time, and the roster screen answered by refusing to open
    /// character creation -- the client evidently treats this batch as a unit.
    ///
    /// Which member the client actually requires has not been isolated, and the batch is reproduced
    /// whole rather than trimmed to a guess. Narrowing it down means removing one at a time and
    /// re-testing, which is cheap but has not been done; until then the honest statement is that
    /// the batch works, not that every member is load-bearing.
    ///
    /// Honour standing is also sent with the roster. Both are kept: the reference server sends it
    /// in both places too, and the second one carries the value computed from the roster.
    /// </remarks>
    private async ValueTask<IReadOnlyList<IGameNotiMessage>> BuildAccountSettingsAsync(
        Dfo.Server.Data.Accounts.AccountId accountId,
        CancellationToken cancellationToken)
    {
        // Settings only. The full account entry load demands a selectable roster and throws for an
        // account that owns no character, which is exactly the account that is logging in for the
        // first time -- and that throw silently cost the connection.
        var settings = await accountEntryState.LoadSettingsAsync(accountId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            new AccountGameOptionsMessage(settings),
            new LoginHotkeyStateMessage(settings),
            new AuctionServiceStateNotice(0x00, Open: true),
            new AuctionServiceStateNotice(0x01, Open: true),
            new HonorLevelInfoNotice(AccountHonorLevel.Initial),
            new InformNoticeNotice(settings.SeenNotices),
        ];
    }

    private async ValueTask RejectAsync(NetworkConnectionContext connection, string category)
    {
        logger.LogWarning("Game login rejected: {Category}", category);
        onlineUsers.TryDisconnectConnection(connection.ConnectionId);
        await closer.RequestCloseAsync(connection, CancellationToken.None).ConfigureAwait(false);
    }
}
