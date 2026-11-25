using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDomainService
{
    ValueTask<GuildMutationResult> CreateAsync(CharacterId actor, string name, string promotion, CancellationToken ct);
    ValueTask<GuildMutationResult> ApplyAsync(CharacterId actor, GuildId guild, string message, CancellationToken ct);
    ValueTask<GuildMutationResult> ApproveAsync(CharacterId actor, CharacterId applicant, CancellationToken ct);
    ValueTask<GuildMutationResult> LeaveAsync(CharacterId actor, CancellationToken ct);
    ValueTask<GuildMutationResult> RemoveMemberAsync(CharacterId actor, CharacterId member, CancellationToken ct);
    ValueTask<GuildMutationResult> DisbandAsync(CharacterId actor, CancellationToken ct);

    /// <summary>Clears a nonmember's application within the same transaction as the character deletion.</summary>
    ValueTask<bool> PrepareCharacterDeletionAsync(CharacterId character, CancellationToken ct);
}

public interface IGuildQueryDomainService
{
    ValueTask<GuildPublicIdentity?> FindPublicIdentityAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildMembership?> FindMembershipAsync(CharacterId character, CancellationToken ct);
    ValueTask<GuildApplication?> FindApplicationAsync(CharacterId applicant, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildApplication>> ReadApplicationsAsync(CharacterId leader, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildMember>> ReadRosterAsync(CharacterId viewer, CancellationToken ct);
}
