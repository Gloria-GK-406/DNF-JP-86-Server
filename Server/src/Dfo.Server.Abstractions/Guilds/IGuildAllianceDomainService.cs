using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildAllianceDomainService
{
    ValueTask<GuildAlliancePeer?> FindAllyAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAllianceApplication>?> ReadApplicationsAsync(CharacterId actor, bool incoming, CancellationToken ct);
    ValueTask<GuildAllianceResult> ChangeAsync(CharacterId actor, GuildAllianceAction action, GuildId target,
        string? targetName, string? message, CancellationToken ct);
}

public interface IGuildAllianceStore
{
    ValueTask<GuildAlliancePeer?> FindGuildAsync(GuildId guild, CancellationToken ct);
    ValueTask<GuildAlliancePeer?> FindAllyAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildAllianceApplication>> ReadApplicationsAsync(GuildId guild, bool incoming, CancellationToken ct);
    ValueTask<bool> TryApplyAsync(GuildId sender, GuildId target, string message, long appliedAt, CancellationToken ct);
    ValueTask<bool> TryRemoveApplicationAsync(GuildId sender, GuildId target, CancellationToken ct);
    ValueTask<bool> TryFormAsync(GuildId sender, GuildId target, CancellationToken ct);
    ValueTask<bool> TryDissolveAsync(GuildId guild, GuildId ally, CancellationToken ct);
}
