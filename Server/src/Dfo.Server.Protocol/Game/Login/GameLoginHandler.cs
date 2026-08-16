using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AuctionService;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.InformNotice;
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
    private async ValueTask SendAccountSettingsAsync(
        NetworkConnectionId connectionId,
        Dfo.Server.Data.Accounts.AccountId accountId,
        CancellationToken cancellationToken)
    {
        // Settings only. The full account entry load demands a selectable roster and throws for an
        // account that owns no character, which is exactly the account that is logging in for the
        // first time -- and that throw silently cost the connection.
        var settings = await accountEntryState.LoadSettingsAsync(accountId, cancellationToken)
            .ConfigureAwait(false);

        IGameNotiMessage[] batch =
        [
            new AccountGameOptionsMessage(settings),
            new LoginHotkeyStateMessage(settings),
            new AuctionServiceStateNotice(0x00, Open: true),
            new AuctionServiceStateNotice(0x01, Open: true),
            new HonorLevelInfoNotice(AccountHonorLevel.Initial),
            InformNoticeNotice.Empty,
        ];

        foreach (var message in batch)
        {
            await sender.SendNotiAsync(connectionId, message, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
