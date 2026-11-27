using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildMembershipDomainService
{
    ValueTask<GuildMutationStatus> CheckCreationAsync(CharacterId actor, string name, CancellationToken ct);
    ValueTask<GuildMutationResult> CreateAsync(CharacterId actor, string name, string promotion, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetMemberMemoAsync(CharacterId actor, string memo, CancellationToken ct);
    ValueTask<GuildRankAssignmentResult?> AssignRankAsync(CharacterId actor, byte[] name, byte rank, CancellationToken ct);
    ValueTask<CharacterId?> TransferLeadershipAsync(CharacterId actor, byte[] targetName, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRankNameAsync(CharacterId actor, byte rank, string name, CancellationToken ct);
    ValueTask<GuildMutationStatus> SetRankPermissionsAsync(CharacterId actor, byte rank, uint permissions, CancellationToken ct);
    ValueTask<GuildMutationResult> ApplyAsync(CharacterId actor, GuildId guild, string message, CancellationToken ct);
    ValueTask<GuildMutationResult> ApproveAsync(CharacterId actor, CharacterId applicant, CancellationToken ct);
    ValueTask<GuildMutationResult> AcceptInvitationAsync(GuildId guild, CharacterId inviter, CharacterId invitee, CancellationToken ct);
    ValueTask<GuildMutationResult> RejectApplicationAsync(CharacterId actor, CharacterId applicant, CancellationToken ct);
    ValueTask<GuildMutationResult> CancelApplicationAsync(CharacterId actor, GuildId guild, CancellationToken ct);
    ValueTask<GuildMutationResult> LeaveAsync(CharacterId actor, CancellationToken ct);
    ValueTask<GuildMutationResult> RemoveMemberAsync(CharacterId actor, CharacterId member, CancellationToken ct);
    ValueTask<GuildMutationResult> DisbandAsync(CharacterId actor, CancellationToken ct);

    /// <summary>Clears a nonmember's application within the same transaction as the character deletion.</summary>
    ValueTask<bool> PrepareCharacterDeletionAsync(CharacterId character, CancellationToken ct);
}
