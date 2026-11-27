using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildAllianceDomainService(IGuildDomainStore guilds, IGuildAllianceStore alliances,
    TimeProvider clock) : IGuildAllianceDomainService
{
    public ValueTask<GuildAlliancePeer?> FindAllyAsync(GuildId guild, CancellationToken ct) =>
        alliances.FindAllyAsync(guild, ct);

    public async ValueTask<IReadOnlyList<GuildAllianceApplication>?> ReadApplicationsAsync(CharacterId actor,
        bool incoming, CancellationToken ct) =>
        await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is { Member.IsLeader: true } membership
            ? await alliances.ReadApplicationsAsync(membership.Guild.Id, incoming, ct).ConfigureAwait(false) : null;

    public async ValueTask<GuildAllianceResult> ChangeAsync(CharacterId actor, GuildAllianceAction action, GuildId target,
        string? targetName, string? message, CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildAllianceStatus.Forbidden);

        var guild = membership.Guild.Id;
        if (target.Value == 0 || target == guild
            || await alliances.FindGuildAsync(target, ct).ConfigureAwait(false) is not { } other)
            return new(GuildAllianceStatus.InvalidTarget);

        if (action is GuildAllianceAction.Apply or GuildAllianceAction.Approve
            && (await alliances.FindAllyAsync(guild, ct).ConfigureAwait(false) is not null
                || await alliances.FindAllyAsync(target, ct).ConfigureAwait(false) is not null))
            return new(GuildAllianceStatus.AlreadyAllied);

        var status = action switch
        {
            GuildAllianceAction.Apply => await ApplyAsync(guild, other, targetName, message, ct).ConfigureAwait(false),
            GuildAllianceAction.Withdraw => await RemoveAsync(guild, target, ct).ConfigureAwait(false),
            GuildAllianceAction.Approve => await ApproveAsync(target, guild, ct).ConfigureAwait(false),
            GuildAllianceAction.Reject => await RemoveAsync(target, guild, ct).ConfigureAwait(false),
            GuildAllianceAction.Dissolve => await alliances.TryDissolveAsync(guild, target, ct).ConfigureAwait(false)
                ? GuildAllianceStatus.Success : GuildAllianceStatus.AllianceMissing,
            _ => GuildAllianceStatus.InvalidTarget,
        };
        return new(status, status == GuildAllianceStatus.Success ? other : null);
    }

    private async ValueTask<GuildAllianceStatus> ApplyAsync(GuildId guild, GuildAlliancePeer other, string? name,
        string? message, CancellationToken ct)
    {
        if (name is null || GuildProfileRuleService.NameKey(name) is not { } key || GuildProfileRuleService.NameKey(other.Name) != key)
            return GuildAllianceStatus.InvalidTarget;
        if (message is null || !GuildProfileRuleService.AllowsRequestMessage(message))
            return GuildAllianceStatus.InvalidMessage;
        return await alliances.TryApplyAsync(guild, other.Id, message, clock.GetUtcNow().ToUnixTimeSeconds(), ct)
            .ConfigureAwait(false) ? GuildAllianceStatus.Success : GuildAllianceStatus.PendingOrListFull;
    }

    private async ValueTask<GuildAllianceStatus> ApproveAsync(GuildId sender, GuildId target, CancellationToken ct)
    {
        if (!await alliances.TryFormAsync(sender, target, ct).ConfigureAwait(false))
            return GuildAllianceStatus.ApplicationMissing;
        if (!await alliances.TryRemoveApplicationAsync(sender, target, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The approved guild alliance application disappeared in the same transaction.");
        return GuildAllianceStatus.Success;
    }

    private async ValueTask<GuildAllianceStatus> RemoveAsync(GuildId sender, GuildId target, CancellationToken ct) =>
        await alliances.TryRemoveApplicationAsync(sender, target, ct).ConfigureAwait(false)
            ? GuildAllianceStatus.Success : GuildAllianceStatus.ApplicationMissing;
}
