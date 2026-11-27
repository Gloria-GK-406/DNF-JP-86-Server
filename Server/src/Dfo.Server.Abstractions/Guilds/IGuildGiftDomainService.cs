using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildGiftDomainService
{
    ValueTask<GuildGiftResult> SendAsync(CharacterId actor, GuildId recipient, ushort productId, CancellationToken ct);
    ValueTask<GuildGiftResult> ClaimAsync(CharacterId actor, GuildGiftKey key, CancellationToken ct);
    ValueTask<GuildGiftView?> ReadAsync(CharacterId actor, bool history, CancellationToken ct);
}

public interface IGuildGiftStore
{
    ValueTask<bool> HasSentAsync(GuildId sender, GuildId recipient, DateOnly day, CancellationToken ct);
    ValueTask AddAsync(GuildGift gift, DateOnly day, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildGift>> ReadAsync(GuildId guild, bool history, CancellationToken ct);
    ValueTask<GuildGift?> FindPendingAsync(GuildId recipient, GuildGiftKey key, CancellationToken ct);
    ValueTask<bool> TryMarkClaimedAsync(GuildId recipient, GuildGiftKey key, long claimedAt, CancellationToken ct);
}
