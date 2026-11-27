using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildProfileDomainService
{
    ValueTask<GuildMutationStatus> SetPromotionAsync(CharacterId actor, string promotion, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetAnnouncementAsync(CharacterId actor, string announcement, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetMarkAsync(CharacterId actor, byte mark, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetMedalAuraAsync(CharacterId actor, GuildMedalAura aura, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRecommendedChannelAsync(CharacterId actor, int channel, CancellationToken ct);
}
