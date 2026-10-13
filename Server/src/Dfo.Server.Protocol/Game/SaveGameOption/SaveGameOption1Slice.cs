using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveGameOption;

public sealed record SaveGameOption1Request(GameMessageMetadata Metadata, byte[] Options) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveGameOption1;
}
internal static class SaveGameOption1PayloadCodec
{
    public static SaveGameOption1Request Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new(metadata, ClientSettingsPayload.Decode(body, 150));
}
internal sealed class SaveGameOption1MessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        SaveGameOption1PayloadCodec.Decode(metadata, body);
}
public sealed class SaveGameOption1Handler(IOnlineUserQueryDomainService users,
    ISelectedSessionDomainService sessions, ICommandBus commands, ILogger<SaveGameOption1Handler> logger)
    : ClientMessageHandler<SaveGameOption1Request>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveGameOption1Request message, CancellationToken ct)
    {
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var saved = await commands.SendAsync(new SaveClientSettingsCommand(origin, ClientSettingKind.Main, message.Options), ct);
        logger.LogInformation("Client settings main account={AccountId} connection={ConnectionId} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, message.Options.Length, saved);
        // Report-only: A21 registers no CMD response handler for this upload.
    }
}
