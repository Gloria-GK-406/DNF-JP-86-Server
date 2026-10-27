using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveHotkeys;

public sealed record GameSaveHotkeysCommand(NetworkDispatchContext Dispatch, SaveHotkeysRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSaveHotkeysCommandHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IClientSettingsDomainService settings, ICommandWorkUnits units, ILogger<GameSaveHotkeysCommandHandler> logger)
    : ICommandHandler<GameSaveHotkeysCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSaveHotkeysCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return false;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var profile = settings.CaptureHotkeyUpload(origin);
        if (profile is null)
        {
            logger.LogWarning("Client settings hotkeys ignored: unknown profile account={AccountId} connection={ConnectionId} bytes={Bytes}",
                account.Value, context.Connection.ConnectionId, message.Payload.Length);
            return false;
        }
        var saved = await units.WriteAsync(execution, new("SaveHotkeys",
            origin.SelectedSession is { } selected ? [new(selected, context.Connection, message.Metadata)] : []),
            (_, token) => settings.SaveHotkeysAsync(profile, message.Payload, token), ct).ConfigureAwait(false);
        logger.LogInformation("Client settings hotkeys account={AccountId} connection={ConnectionId} profile={Profile} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, profile.KeyType, message.Payload.Length, saved);
        return saved;
    }
}
