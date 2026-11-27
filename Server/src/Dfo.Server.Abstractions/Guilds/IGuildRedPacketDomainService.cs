using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildRedPacketRuleSource
{
    GuildRedPacketRules? Read();
}

public interface IGuildRedPacketDomainService
{
    ValueTask<GuildRedPacketList?> ReadAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRedPacketClaim>> ReadHistoryAsync(CharacterId actor, uint packet, CancellationToken ct);
    ValueTask<GuildRedPacketResult> SendAsync(SelectedSessionIdentity actor, int selection, byte shares,
        IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask<GuildRedPacketResult> TakeAsync(SelectedSessionIdentity actor, uint packet, CancellationToken ct);
    ValueTask ExpireAsync(CancellationToken ct);
    ValueTask<int> CountAvailableAsync(CharacterId character, AccountId account, CancellationToken ct);
}

public interface IGuildRedPacketPurchasePolicy
{
    CeraShopQuote Prepare(CeraShopQuote quote, DateTimeOffset now);
    ValueTask<string?> RecordPurchaseAsync(CharacterId character, CeraShopQuote quote, DateTimeOffset now, CancellationToken ct);
}

public interface IGuildRedPacketStore
{
    ValueTask<int> PurchasedAsync(CharacterId character, int day, CancellationToken ct);
    ValueTask RecordPurchaseAsync(CharacterId character, int day, int amount, CancellationToken ct);
    ValueTask<int> CountActiveAsync(GuildId guild, long now, CancellationToken ct);
    ValueTask<int> CountTakenAsync(AccountId account, int day, CancellationToken ct);
    ValueTask<GuildRedPacket?> FindAsync(uint packet, GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRedPacket>> ListAsync(GuildId guild, long now, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRedPacketClaim>> HistoryAsync(uint packet, CancellationToken ct);
    ValueTask<IReadOnlySet<uint>> TakenPacketsAsync(GuildId guild, AccountId account, CancellationToken ct);
    ValueTask<bool> HasTakenAsync(uint packet, AccountId account, CancellationToken ct);
    ValueTask AddAsync(GuildRedPacket packet, CancellationToken ct);
    ValueTask<bool> TakeAsync(GuildRedPacket packet, GuildRedPacketClaim claim, int day, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildRedPacket>> FindDueAsync(long now, int count, CancellationToken ct);
    ValueTask SettleAsync(GuildRedPacket packet, long now, int refunded, int points, CancellationToken ct);
}
