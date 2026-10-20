using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteCharacterNameTagDomainStore(SqliteDatabase database) : ICharacterNameTagDomainStore
{
    public ValueTask<CharacterNameTag?> ReadAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        database.ReadFirstAsync("SELECT name_tag_item_id,name_tag_expire_time FROM characters WHERE account_id=@account AND character_id=@character AND delete_flag=0",
            p => { p.AddWithValue("@account", account.Value); p.AddWithValue("@character", character.Value); },
            r => new CharacterNameTag(checked((int)r.GetInt64(0)), r.GetInt64(1)), ct);

    public async ValueTask<bool> TryWriteAsync(AccountId account, CharacterId character, CharacterNameTag expected,
        CharacterNameTag next, CancellationToken ct) =>
        await database.ExecuteAsync("""
            UPDATE characters SET name_tag_item_id=@item,name_tag_expire_time=@end
            WHERE account_id=@account AND character_id=@character AND delete_flag=0
              AND name_tag_item_id=@oldItem AND name_tag_expire_time=@oldEnd
            """, p =>
            {
                p.AddWithValue("@account", account.Value); p.AddWithValue("@character", character.Value);
                p.AddWithValue("@item", next.ItemId); p.AddWithValue("@end", next.ExpiresAtUnixSeconds);
                p.AddWithValue("@oldItem", expected.ItemId); p.AddWithValue("@oldEnd", expected.ExpiresAtUnixSeconds);
            }, ct).ConfigureAwait(false) == 1;
}
