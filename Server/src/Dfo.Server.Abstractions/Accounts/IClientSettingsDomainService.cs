using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.Accounts;

public interface IClientSettingsDomainService
{
    ValueTask<bool> SaveMainAsync(ClientSettingsOrigin origin, byte[] payload, CancellationToken ct);
    ValueTask<bool> SaveQuickChatAsync(ClientSettingsOrigin origin, byte bank, byte[] payload, CancellationToken ct);
    ValueTask<bool> SaveHotkeysAsync(ClientHotkeyUploadContext context, byte[] payload, CancellationToken ct);
    ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId accountId, CancellationToken ct);
    bool ActivateHotkeyProfile(ClientSettingsOrigin origin, byte keyType);
    ClientHotkeyUploadContext? CaptureHotkeyUpload(ClientSettingsOrigin origin);
    void ForgetConnection(NetworkConnectionId connectionId);
    void ForgetConnection(GameSessionIdentity connection);
}
