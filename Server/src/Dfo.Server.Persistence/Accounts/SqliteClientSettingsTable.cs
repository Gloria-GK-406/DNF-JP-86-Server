using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Accounts;

internal sealed class SqliteClientSettingsTable(SqliteDatabase database) : IClientSettingsTable
{
    public async ValueTask SaveMainAsync(AccountId account, byte[] payload, CancellationToken ct) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO account_settings(account_id,main_game_option) VALUES(@id,@blob) " +
            "ON CONFLICT(account_id) DO UPDATE SET main_game_option=excluded.main_game_option;",
            p => { p.AddWithValue("@id", account.Value); p.AddWithValue("@blob", payload); }, ct);

    public async ValueTask SaveQuickChatAsync(AccountId account, byte bank, byte[] payload, CancellationToken ct)
    {
        var column = bank switch { 0 => "quickchat_bank0", 1 => "quickchat_bank1", _ => throw new ArgumentOutOfRangeException(nameof(bank)) };
        _ = await database.ExecuteAsync(
            $"INSERT INTO account_settings(account_id,{column}) VALUES(@id,@blob) " +
            $"ON CONFLICT(account_id) DO UPDATE SET {column}=excluded.{column};",
            p => { p.AddWithValue("@id", account.Value); p.AddWithValue("@blob", payload); }, ct);
    }

    public async ValueTask SaveHotkeysAsync(AccountId account, byte type, byte[] payload, CancellationToken ct) =>
        _ = await database.ExecuteAsync(
            "INSERT INTO account_hotkey_options(account_id,key_type,payload) VALUES(@id,@type,@blob) " +
            "ON CONFLICT(account_id,key_type) DO UPDATE SET payload=excluded.payload;",
            p => { p.AddWithValue("@id", account.Value); p.AddWithValue("@type", type); p.AddWithValue("@blob", payload); }, ct);

    public ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId account, CancellationToken ct) =>
        database.ReadAllAsync("SELECT key_type,payload FROM account_hotkey_options WHERE account_id=@id ORDER BY key_type;",
            p => p.AddWithValue("@id", account.Value),
            r => new HotkeyProfileState(checked((byte)r.GetInt32(0)), ReadPayload((byte[])r.GetValue(1))), ct);

    private static byte[] ReadPayload(byte[] payload) => payload.Length <= 65536
        ? payload : throw new InvalidDataException("Hotkey text exceeds the supported size.");
}
