using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Persistence;

public interface IClientSettingsTable
{
    ValueTask<byte[]?> ReadMainAsync(AccountId account, CancellationToken ct);
    ValueTask SaveMainAsync(AccountId account, byte[] payload, CancellationToken ct);
    ValueTask SaveQuickChatAsync(AccountId account, byte bank, byte[] payload, CancellationToken ct);
    ValueTask SaveHotkeysAsync(AccountId account, byte type, byte[] payload, CancellationToken ct);
    ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId account, CancellationToken ct);
}
