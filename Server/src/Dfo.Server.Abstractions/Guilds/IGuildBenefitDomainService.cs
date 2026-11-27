using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildBenefitDomainService
{
    ValueTask<IReadOnlyList<GuildPurchasedContent>> ReadContentsAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAttributeBonus>> ReadAttributeBonusesAsync(GuildId guild, CancellationToken ct);
    ValueTask<double> ReadMonsterExperienceRateAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMutationResult> ConsumeTeleportAsync(CharacterId actor, CancellationToken ct);
}
