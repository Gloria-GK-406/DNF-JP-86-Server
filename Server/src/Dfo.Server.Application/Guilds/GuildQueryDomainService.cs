using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildQueryDomainService(IGuildDomainStore store) : IGuildQueryDomainService
{
    public async ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct) =>
        await store.FindMembershipAsync(character, ct).ConfigureAwait(false) is { Guild: var guild }
            ? new(guild.Id, guild.Name, guild.Level)
            : null;

    public ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct) =>
        store.FindMembershipAsync(character, ct);

    public ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct) =>
        store.FindApplicationAsync(applicant, ct);

    public ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId leader, CancellationToken ct) =>
        store.ReadApplicationsAsync(leader, ct);

    public ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterAsync(viewer, ct);

    public ValueTask<IReadOnlyList<GuildRosterEntry>> ReadRosterEntriesAsync(CharacterId viewer, CancellationToken ct) =>
        store.ReadRosterEntriesAsync(viewer, ct);
}
