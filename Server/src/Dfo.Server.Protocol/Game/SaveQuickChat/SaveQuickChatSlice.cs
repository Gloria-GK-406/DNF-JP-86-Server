using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveQuickChat;

public sealed record SaveQuickChatRequest(GameMessageMetadata Metadata, byte Bank, byte[] Payload) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveQuickChat;
}

internal sealed class SaveQuickChatDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 5 || body[0] > 1) throw new InvalidDataException("Quick-chat bank must be 0 or 1.");
        return new SaveQuickChatRequest(metadata, body[0], ClientSettingsPayload.Decode(body[1..], 300));
    }
}

public sealed class SaveQuickChatHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    ICommandBus commands, ILogger<SaveQuickChatHandler> logger) : ClientMessageHandler<SaveQuickChatRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveQuickChatRequest message, CancellationToken ct)
    {
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var saved = await commands.SendAsync(new SaveClientSettingsCommand(origin, ClientSettingKind.QuickChat, message.Payload, message.Bank), ct);
        logger.LogInformation("Client settings quickchat account={AccountId} connection={ConnectionId} bank={Bank} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, message.Bank, message.Payload.Length, saved);
    }
}
