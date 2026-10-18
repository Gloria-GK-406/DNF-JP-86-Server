using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SaveHotkeys;

public sealed record SaveHotkeysRequest(GameMessageMetadata Metadata, byte[] Payload) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveHotkeys;
}

internal sealed class SaveHotkeysDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SaveHotkeysRequest(metadata, ClientSettingsPayload.Decode(body));
}

public sealed class SaveHotkeysHandler(IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IClientSettingsDomainService settings, ICommandBus commands, ILogger<SaveHotkeysHandler> logger)
    : ClientMessageHandler<SaveHotkeysRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SaveHotkeysRequest message, CancellationToken ct)
    {
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user?.AccountId is not { } account) return;
        var origin = new ClientSettingsOrigin(user.SessionIdentity, account, user.Generation, sessions.FindByConnection(context.Connection.ConnectionId));
        var profile = settings.CaptureHotkeyUpload(origin);
        if (profile is null)
        {
            logger.LogWarning("Client settings hotkeys ignored: unknown profile account={AccountId} connection={ConnectionId} bytes={Bytes}",
                account.Value, context.Connection.ConnectionId, message.Payload.Length);
            return;
        }
        var saved = await commands.SendAsync(new SaveClientSettingsCommand(origin, ClientSettingKind.Hotkeys, message.Payload, Hotkeys: profile), ct);
        logger.LogInformation("Client settings hotkeys account={AccountId} connection={ConnectionId} profile={Profile} bytes={Bytes} saved={Saved}",
            account.Value, context.Connection.ConnectionId, profile.KeyType, message.Payload.Length, saved);
    }
}
