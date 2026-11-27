using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildPointAccount
{
    ValueTask<bool> CanCreditAsync(GuildId guild, int amount, CancellationToken ct);
    ValueTask<bool> TryCreditAsync(GuildId guild, int amount, CancellationToken ct);
    ValueTask<bool> TrySpendAsync(GuildId guild, int amount, CancellationToken ct);
}
