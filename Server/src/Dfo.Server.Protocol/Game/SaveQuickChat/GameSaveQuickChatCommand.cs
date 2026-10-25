using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveQuickChat;

public sealed record GameSaveQuickChatCommand(NetworkDispatchContext Dispatch, SaveQuickChatRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSaveQuickChatCommandHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IClientSettingsDomainService settings, ICommandWorkUnits units, ILogger<GameSaveQuickChatCommandHandler> logger)
    : ICommandHandler<GameSaveQuickChatCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSaveQuickChatCommand command, CancellationToken ct)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return false;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var saved = await units.WriteAsync(execution, new("SaveQuickChat",
            origin.SelectedSession is { } selected ? [new(selected, context.Connection, message.Metadata)] : []),
            (_, token) => settings.SaveQuickChatAsync(origin, message.Bank, message.Payload, token), ct).ConfigureAwait(false);
        logger.LogInformation("Client settings quickchat account={AccountId} connection={ConnectionId} bank={Bank} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, message.Bank, message.Payload.Length, saved);
        return saved;
    }
}
