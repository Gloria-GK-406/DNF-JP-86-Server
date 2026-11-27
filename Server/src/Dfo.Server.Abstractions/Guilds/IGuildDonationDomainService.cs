using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDonationDomainService
{
    ValueTask<GuildMutationResult> DonateGoldAsync(CharacterId actor, int amount, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildDonationEntry>?> ReadDonationHistoryAsync(CharacterId viewer, int limit, CancellationToken ct);
}
