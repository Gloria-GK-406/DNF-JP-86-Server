using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Accounts;

public sealed record HotkeyProfileState(byte KeyType, byte[] Payload);

public sealed record ClientSettingsOrigin(
    GameSessionIdentity Connection, AccountId AccountId, long Generation,
    SelectedSessionIdentity? SelectedSession);

public sealed record ClientHotkeyUploadContext(ClientSettingsOrigin Origin, byte KeyType, Guid ProfileToken);
