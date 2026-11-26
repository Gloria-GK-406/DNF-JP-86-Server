using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDomainService(
    IGuildDomainStore store,
    GuildRuleService rules,
    ICharacterDomainService characters,
    ICharacterTable characterTable,
    TimeProvider time) : IGuildDomainService
{
    public async ValueTask<GuildMutationResult> CreateAsync(
        CharacterId actor, string name, string promotion, CancellationToken ct)
    {
        if (rules.NameKey(name) is not { } nameKey)
            return new(GuildMutationStatus.InvalidName);
        if (!rules.IsValidText(promotion))
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
        rules.NameKey(name) is { } key
            ? CheckCreationEligibilityAsync(actor, key, ct)
            : ValueTask.FromResult(GuildMutationStatus.InvalidName);

    public async ValueTask<GuildMutationStatus> SetPromotionAsync(CharacterId actor, string promotion, CancellationToken ct)
    {
        if (!rules.IsValidPromotion(promotion))
            return GuildMutationStatus.InvalidText;
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetPromotionAsync(membership.Guild.Id, promotion, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
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
        if (target is null || target.IsLeader || target.Character == actor)
            return null;

        if (!actorMembership.Member.IsLeader)
        {
            if (actorMembership.Member.Rank != GuildRankSettings.ViceLeader ||
                target.Rank <= GuildRankSettings.ViceLeader || rank <= GuildRankSettings.ViceLeader)
                return null;

            var settings = await store.ReadRankSettingsAsync(guild, ct).ConfigureAwait(false);
            var permissions = settings.FirstOrDefault(setting => setting.Rank == GuildRankSettings.ViceLeader)?.PermissionMask ?? 0;
            if ((permissions & GuildRankSettings.AppointMembersPermission) == 0)
                return null;
        }

        var changed = target.Rank != rank;
        if (changed)
            await store.SetMemberRankAsync(guild, target.Character, rank, ct).ConfigureAwait(false);
        return new(target.Character, rank, changed);
    }

    public async ValueTask<GuildMutationStatus> SetRankNameAsync(CharacterId actor, byte rank, string name, CancellationToken ct)
    {
        if (!GuildRankSettings.IsMemberRank(rank) || !rules.IsValidRankName(name))
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

    public async ValueTask<GuildMutationStatus> SetRecommendedChannelAsync(CharacterId actor, int channel, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channel);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return GuildMutationStatus.LeaderRequired;

        await store.SetRecommendedChannelAsync(membership.Guild.Id, channel, ct).ConfigureAwait(false);
        return GuildMutationStatus.Success;
    }

    public async ValueTask<GuildMutationResult> DonateGoldAsync(CharacterId actor, int amount, CancellationToken ct)
    {
        if (amount <= 0)
            return new(GuildMutationStatus.InvalidDonationAmount);
        if (amount > GuildRuleService.MaximumDonationGold)
            return new(GuildMutationStatus.DonationTooLarge);
        if (rules.DonationGoldPerPoint() is not { } rate)
            return new(GuildMutationStatus.DonationUnavailable);
        if (amount % rate != 0)
            return new(GuildMutationStatus.InvalidDonationAmount);
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildMutationStatus.NotMember);

        var points = amount / rate;
        if (membership.Guild.GmPoints > int.MaxValue - points ||
            membership.Member.DonationPoints > int.MaxValue - points)
            return new(GuildMutationStatus.ContributionLimit);
        if (await characters.Get(actor).TrySpendGoldAsync(amount, ct).ConfigureAwait(false) is not { } remaining)
            return new(GuildMutationStatus.InsufficientGold);

        await store.AddDonationAsync(membership.Guild.Id, actor, amount, points, time.GetUtcNow(), ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, membership.Guild.Id, remaining);
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
        if (!rules.IsValidText(message))
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
        if (await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildMutationStatus.LeaderRequired);
        var guild = membership.Guild.Id;
        if (await store.FindApplicationAsync(applicant, ct).ConfigureAwait(false) is not { } application || application.Guild != guild)
            return new(GuildMutationStatus.ApplicationNotFound);
        if (await characterTable.FindOwnerAsync(applicant, ct).ConfigureAwait(false) is null)
            return new(GuildMutationStatus.CharacterUnavailable);
        if (await store.FindMembershipAsync(applicant, ct).ConfigureAwait(false) is not null)
            return new(GuildMutationStatus.AlreadyMember);

        await AddMemberAsync(guild, applicant, isLeader: false, time.GetUtcNow(), ct).ConfigureAwait(false);
        return new(GuildMutationStatus.Success, guild, AffectedCharacters: [actor, applicant]);
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
        var affected = await store.AffectedCharactersAsync(guild, ct).ConfigureAwait(false);
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
