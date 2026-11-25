using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillLearningDomainService(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    ICharacterSkillStateDomainService state,
    ICharacterSkillDomainStoreService skillStore,
    IEnumerable<ICharacterSkillGroupEditPolicy> groupEditPolicies,
    IDomainTransactionAccessor transactions) : ICharacterSkillLearningDomainService
{
    public async ValueTask<bool> SwapSlotsAsync(SelectedSessionIdentity session, int pageIndex, int first, int second, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill slot changes require a transaction.");
        if (pageIndex < 0 || !sessions.IsInWorld(session)) return false;
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        if (view is null) return false;
        var group = view.World.Channel.SkillGroup;
        var build = new SkillBuildId(pageIndex);
        if (await RefuseEditAsync(session, group, build, CharacterSkillGroupEditOperation.SwapSlots, ct)
            .ConfigureAwait(false) != CharacterSkillLearningRejection.None)
            return false;
        return await skillStore.Get(session.CharacterId, group, build)
            .SwapSlotsAsync(first, second, ct).ConfigureAwait(false);
    }

    public ValueTask<int?> SwitchAsync(SelectedSessionIdentity session, int expectedCurrent, int target, CancellationToken ct) =>
        SwitchCoreAsync(session, expectedCurrent, target, ct);

    public ValueTask<int?> ToggleAsync(SelectedSessionIdentity session, int? expectedCurrent, CancellationToken ct) =>
        SwitchCoreAsync(session, expectedCurrent, null, ct);

    private async ValueTask<int?> SwitchCoreAsync(SelectedSessionIdentity session, int? expectedCurrent, int? target,
        CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill switching requires a transaction.");
        if (target is < 0 || !sessions.IsInWorld(session))
            return null;
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        if (view is null) return null;
        var group = view.World.Channel.SkillGroup;
        if (group == SkillGroup.Pve && onlineCharacters.StandingOf(session) is null)
            return null;
        if (await RefuseGroupEditAsync(session, group, CharacterSkillGroupEditOperation.SwitchBuild, ct)
            .ConfigureAwait(false) != CharacterSkillLearningRejection.None)
            return null;

        var builds = await state.BuildsAsync(session.CharacterId, ct, group).ConfigureAwait(false);
        var current = builds.Active.Value;
        if (expectedCurrent is { } expected && expected != current)
            return null;
        var next = target ?? (current is 0 or 1 ? 1 - current : -1);
        if (next < 0 || !builds.IsUnlocked(new(next)))
            return null;
        await state.ActivateAsync(session.CharacterId, new(next), ct, group).ConfigureAwait(false);
        return next;
    }

    public async ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session, int pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, byte mode, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill learning requires a domain transaction.");
        if (!sessions.IsInWorld(session)
            || await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false) is not { } selected)
            return new(CharacterSkillLearningRejection.NoSession);
        if (!CharacterSkillDomainService.IsValidLearningRequest(changes))
            return new(CharacterSkillLearningRejection.InvalidRequest);
        var group = selected.World.Channel.SkillGroup;
        if (mode != 0 || (group == SkillGroup.Pve && onlineCharacters.StandingOf(session) is null)
            || (group != SkillGroup.Pve && changes.Any(change => change.Refund != 0)))
            return new(CharacterSkillLearningRejection.UnsupportedScenario);
        if (pageIndex < 0)
            return new(CharacterSkillLearningRejection.PageUnavailable);
        var build = new SkillBuildId(pageIndex);
        var refusal = await RefuseEditAsync(session, group, build, CharacterSkillGroupEditOperation.Learn, ct).ConfigureAwait(false);
        if (refusal != CharacterSkillLearningRejection.None)
            return new(refusal);
        return await skillStore.Get(session.CharacterId, group, build)
            .LearnAsync(session, changes, footprint, ct).ConfigureAwait(false);
    }

    private async ValueTask<CharacterSkillLearningRejection> RefuseEditAsync(
        SelectedSessionIdentity session, SkillGroup group, SkillBuildId build,
        CharacterSkillGroupEditOperation operation, CancellationToken ct)
    {
        var refusal = await RefuseGroupEditAsync(session, group, operation, ct).ConfigureAwait(false);
        if (refusal != CharacterSkillLearningRejection.None)
            return refusal;
        var builds = await state.BuildsAsync(session.CharacterId, ct, group).ConfigureAwait(false);
        return builds.Active == build
            ? CharacterSkillLearningRejection.None
            : CharacterSkillLearningRejection.PageUnavailable;
    }

    private ValueTask<CharacterSkillLearningRejection> RefuseGroupEditAsync(
        SelectedSessionIdentity session, SkillGroup group, CharacterSkillGroupEditOperation operation, CancellationToken ct)
    {
        if (group == SkillGroup.Pve)
            return ValueTask.FromResult(CharacterSkillLearningRejection.None);
        var policy = groupEditPolicies.SingleOrDefault(candidate => candidate.Group == group);
        return policy is null
            ? ValueTask.FromResult(CharacterSkillLearningRejection.UnsupportedScenario)
            : policy.RefuseEditAsync(session, operation, ct);
    }
}
