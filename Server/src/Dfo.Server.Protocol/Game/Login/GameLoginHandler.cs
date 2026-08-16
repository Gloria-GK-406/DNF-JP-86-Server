using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.Login;

public sealed class GameLoginHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
    ICommandBus commandBus,
    IServerConfigService serverConfig,
    IAccountEntryStateService accountEntryState) : ClientMessageHandler<GameLoginRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GameLoginRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser is null)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' has no online-user registration.");
        }

        var response = new GameLoginResponse(
            Succeeded: true,
            serverConfig.GetConfig<byte>(GameLoginConfigKeys.Environment),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.ConnectionPort),
            serverConfig.GetConfig<string>(GameLoginConfigKeys.EndpointHost),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.EndpointPortA),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.EndpointPortB));
        var login = await commandBus.SendAsync(
            new LoginGameUserCommand(
                onlineUser.OnlineUserId,
                message.LoginId,
                message.PasswordHash),
            cancellationToken).ConfigureAwait(false);
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            response,
            cancellationToken).ConfigureAwait(false);
        await SendAccountSettingsAsync(
            context.Connection.ConnectionId,
            login.AccountId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes the account-scoped game options and hotkeys immediately after the login response.
    /// </summary>
    /// <remarks>
    /// These two are also sent in the select-character batch, and for a long time that was the only
    /// place this server sent them. That turned out to be too late: the client needs its
    /// account-level input state before the roster screen, and without it the roster screen will
    /// not open character creation at all. The legacy server sent them from here for the same
    /// reason, and its own comment says so -- the select-character copy restores the UI checkboxes
    /// but not the runtime state.
    ///
    /// The ordering is deliberate and matches the legacy server: login response first, then these.
    /// </remarks>
    private async ValueTask SendAccountSettingsAsync(
        NetworkConnectionId connectionId,
        Dfo.Server.Data.Accounts.AccountId accountId,
        CancellationToken cancellationToken)
    {
        var account = await accountEntryState.LoadAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        await sender.SendNotiAsync(
            connectionId,
            new AccountGameOptionsMessage(account.Settings),
            cancellationToken).ConfigureAwait(false);
        await sender.SendNotiAsync(
            connectionId,
            new LoginHotkeyStateMessage(account.Settings),
            cancellationToken).ConfigureAwait(false);
    }
}
