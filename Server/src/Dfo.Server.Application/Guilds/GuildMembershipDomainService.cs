using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildMembershipDomainService(
    IGuildDomainStore store, IGuildWarehouseStore warehouse,
    GuildEconomyRuleService rules,
    ICharacterDomainService characters,
    ICharacterTable characterTable,
    TimeProvider time) : IGuildMembershipDomainService
{
    public async ValueTask<GuildMutationResult> CreateAsync(
        CharacterId actor, string name, string promotion, CancellationToken ct)
    {
        if (GuildTextRules.NameKey(name) is not { } nameKey)
            return new(GuildMutationStatus.InvalidName);
        if (!GuildTextRules.IsValidText(promotion))
            return new(GuildMutationStatus.InvalidText);
        var eligibility = await CheckCreationEligibilityAsync(actor, nameKey, ct).ConfigureAwait(false);
        if (eligibility != GuildMutationStatus.Success)
            return new(eligibility);
        if (rules.CreationCost() is not { } cost)
            return new(GuildMutationStatus.CreationUnavailable);
        if (await characters.Get(actor).TrySpendGoldAsync(cost, ct).ConfigureAwait(false) is not { } remaining)
            return new(GuildMutationStatus.InsufficientGold);

        var at = time.GetUtcNow();
        var guild = await store.InsertGuildAsync(name, nameKey, promotion, at, ct).ConfigureAwait(false);
        await AddMemberAsync(guild.Id, actor, isLeader: true, at, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild.Id, remaining, [actor]);
    }

    public ValueTask<GuildMutationStatus> CheckCreationAsync(CharacterId actor, string name, CancellationToken ct) =>
        GuildTextRules.NameKey(name) is { } key
            ? CheckCreationEligibilityAsync(actor, key, ct)
            : ValueTask.FromResult(GuildMutationStatus.InvalidName);

    public async ValueTask<GuildMutationStatus> SetMemberMemoAsync(CharacterId actor, string memo, CancellationToken ct)
    {
        if (!GuildTextRules.IsValidMemberMemo(memo))
            return GuildMutationStatus.InvalidText;

        return await store.TrySetMemberMemoAsync(actor, memo, ct).ConfigureAwait(false)
            ? GuildMutationStatus.Success
            : GuildMutationStatus.NotMember;
    }

    public async ValueTask<GuildRankAssignmentResult?> AssignRankAsync(
        CharacterId actor, byte[] name, byte rank, CancellationToken ct)
    {
        if (rank is < GuildRankSettings.ViceLeader or > GuildRankSettings.LastRank ||
            name.Length is 0 or > 62 ||
            await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } actorMembership)
            return null;

        var guild = actorMembership.Guild.Id;
        var target = await store.FindRosterMemberAsync(guild, name, ct).ConfigureAwait(false);
        if (target is null || !await GuildRankPolicy.CanAssignRankAsync(store, actorMembership, target, rank, ct).ConfigureAwait(false))
            return null;

        var changed = target.Rank != rank;
        if (changed)
            await store.SetMemberRankAsync(guild, target.Character, rank, ct).ConfigureAwait(false);
        return new(target.Character, rank, changed);
    }

    public async ValueTask<CharacterId?> TransferLeadershipAsync(
        CharacterId actor, byte[] targetName, CancellationToken ct)
    {
        if (targetName.Length is < 1 or > 62 ||
            await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return null;

        var guild = membership.Guild.Id;
        var target = await store.FindRosterMemberAsync(guild, targetName, ct).ConfigureAwait(false);
        if (target is null || target.IsLeader)
            return null;

        await store.TransferLeadershipAsync(guild, actor, target.Character, ct).ConfigureAwait(false);
        return target.Character;
    }

    public async ValueTask<GuildMutationStatus> SetRankNameAsync(CharacterId actor, byte rank, string name, CancellationToken ct)
    {
        if (!GuildRankSettings.IsMemberRank(rank) || !GuildTextRules.IsValidRankName(name))
            return GuildMutationStatus.InvalidRankSetting;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetRankNameAsync(membership.Guild.Id, rank, name, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationStatus> SetRankPermissionsAsync(
        CharacterId actor, byte rank, uint permissions, CancellationToken ct)
    {
        if (!GuildRankSettings.IsMemberRank(rank) || (permissions & ~GuildRankSettings.ValidPermissionMask) != 0)
            return GuildMutationStatus.InvalidRankSetting;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        if (rank == GuildRankSettings.Leader)
            permissions |= GuildRankSettings.LeaderDefaultPermissions;
        await store.SetRankPermissionsAsync(membership.Guild.Id, rank, permissions, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    private async ValueTask<GuildMutationStatus> CheckCreationEligibilityAsync(
        CharacterId actor, string nameKey, CancellationToken ct)
    {
        if (await characterTable.FindOwnerAsync(actor, ct).ConfigureAwait(false) is null)
            return GuildMutationStatus.CharacterUnavailable;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not null)
            return GuildMutationStatus.AlreadyMember;
        if (await store.NameTakenAsync(nameKey, ct).ConfigureAwait(false))
            return GuildMutationStatus.NameTaken;
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationResult> ApplyAsync(CharacterId actor, GuildId guild, string message, CancellationToken ct)
    {
        if (!GuildTextRules.IsValidPromotion(message))
            return new(GuildMutationStatus.InvalidText);
        if (await characterTable.FindOwnerAsync(actor, ct).ConfigureAwait(false) is null)
            return new(GuildMutationStatus.CharacterUnavailable);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not null)
            return new(GuildMutationStatus.AlreadyMember);
        if (!await store.ExistsAsync(guild, ct).ConfigureAwait(false))
            return new(GuildMutationStatus.GuildNotFound);
        if (await store.FindApplicationAsync(actor, ct).ConfigureAwait(false) is not null)
            return new(GuildMutationStatus.ApplicationPending);

        await store.AddApplicationAsync(new(guild, actor, message, time.GetUtcNow()), ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [actor]);
    }

    public async ValueTask<GuildMutationResult> ApproveAsync(CharacterId actor, CharacterId applicant, CancellationToken ct)
    {
        if (await GuildRankPolicy.FindApplicationReviewGuildAsync(store, actor, ct).ConfigureAwait(false) is not { } guild)
            return new(GuildMutationStatus.LeaderRequired);
        if (await store.FindApplicationAsync(applicant, ct).ConfigureAwait(false) is not { } application || application.Guild != guild)
            return new(GuildMutationStatus.ApplicationNotFound);

        return await AdmitMemberAsync(guild, actor, applicant, ct).ConfigureAwait(false);
    }

    public async ValueTask<GuildMutationResult> AcceptInvitationAsync(GuildId guild, CharacterId inviter,
        CharacterId invitee, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(inviter, ct).ConfigureAwait(false) is not { } membership
            || membership.Guild.Id != guild)
            return new(GuildMutationStatus.NotMember);

        return await AdmitMemberAsync(guild, inviter, invitee, ct).ConfigureAwait(false);
    }

    private async ValueTask<GuildMutationResult> AdmitMemberAsync(GuildId guild, CharacterId sponsor,
        CharacterId candidate, CancellationToken ct)
    {
        if (await characterTable.FindOwnerAsync(candidate, ct).ConfigureAwait(false) is null)
            return new(GuildMutationStatus.CharacterUnavailable);
        if (await store.FindMembershipAsync(candidate, ct).ConfigureAwait(false) is not null)
            return new(GuildMutationStatus.AlreadyMember);

        await AddMemberAsync(guild, candidate, isLeader: false, time.GetUtcNow(), ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [sponsor, candidate]);
    }

    public async ValueTask<GuildMutationResult> RejectApplicationAsync(CharacterId actor, CharacterId applicant, CancellationToken ct)
    {
        if (await GuildRankPolicy.FindApplicationReviewGuildAsync(store, actor, ct).ConfigureAwait(false) is not { } guild)
            return new(GuildMutationStatus.LeaderRequired);
        return await ClearApplicationAsync(applicant, guild, ct).ConfigureAwait(false);
    }

    public ValueTask<GuildMutationResult> CancelApplicationAsync(CharacterId actor, GuildId guild, CancellationToken ct) =>
        ClearApplicationAsync(actor, guild, ct);

    private async ValueTask<GuildMutationResult> ClearApplicationAsync(CharacterId applicant, GuildId guild, CancellationToken ct)
    {
        if (await store.FindApplicationAsync(applicant, ct).ConfigureAwait(false) is not { } application || application.Guild != guild)
            return new(GuildMutationStatus.ApplicationNotFound);

        await store.DeleteApplicationAsync(applicant, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [applicant]);
    }

    public async ValueTask<GuildMutationResult> LeaveAsync(CharacterId actor, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildMutationStatus.NotMember);
        if (membership.Member.IsLeader)
            return new(GuildMutationStatus.LeaderCannotLeave);

        var guild = membership.Guild.Id;
        await store.RemoveMemberAsync(guild, actor, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [actor]);
    }

    public async ValueTask<GuildMutationResult> RemoveMemberAsync(CharacterId actor, CharacterId target, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildMutationStatus.LeaderRequired);
        if (actor == target)
            return new(GuildMutationStatus.LeaderCannotLeave);
        var guild = membership.Guild.Id;
        if (await store.FindMembershipAsync(target, ct).ConfigureAwait(false) is not { } member || member.Guild.Id != guild)
            return new(GuildMutationStatus.NotMember);

        await store.RemoveMemberAsync(guild, target, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [actor, target]);
    }

    public async ValueTask<GuildMutationResult> DisbandAsync(CharacterId actor, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildMutationStatus.LeaderRequired);

        var guild = membership.Guild.Id;
        if (await warehouse.HasWarehouseItemsAsync(guild, ct).ConfigureAwait(false))
            return new(GuildMutationStatus.WarehouseNotEmpty);
        if ((await store.ReadRosterCharacterIdsAsync(actor, ct).ConfigureAwait(false)).Count != 1)
            return new(GuildMutationStatus.GuildHasOtherMembers);
        var affected = await store.ReadDisbandRecipientsAsync(guild, ct).ConfigureAwait(false);
        await store.DisbandAsync(guild, ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: affected);
    }

    public async ValueTask<bool> PrepareCharacterDeletionAsync(CharacterId character, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(character, ct).ConfigureAwait(false) is not null)
            return false;
        await store.DeleteApplicationAsync(character, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask AddMemberAsync(GuildId guild, CharacterId member, bool isLeader, DateTimeOffset at, CancellationToken ct)
    {
        await store.AddMemberAsync(guild, member, isLeader, at, ct).ConfigureAwait(false);
        await store.DeleteApplicationAsync(member, ct).ConfigureAwait(false);
    }
}
