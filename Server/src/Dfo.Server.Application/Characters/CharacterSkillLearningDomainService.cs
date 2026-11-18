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
    IDomainTransactionAccessor transactions) : ICharacterSkillLearningDomainService
{
    public async ValueTask<bool> SwapSlotsAsync(SelectedSessionIdentity session, int pageIndex, int first, int second, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill slot changes require a transaction.");
        if (pageIndex < 0 || !sessions.IsInWorld(session)) return false;
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        if (view is null || view.World.Channel.PvpSkillProfileApplies) return false;
        var build = new SkillBuildId(pageIndex);
        if ((await state.BuildsAsync(session.CharacterId, ct).ConfigureAwait(false)).Active != build) return false;
        return await skillStore.Get(session.CharacterId, SkillGroup.Pve, build)
            .SwapSlotsAsync(first, second, ct).ConfigureAwait(false);
    }

    public async ValueTask<int?> SwitchAsync(SelectedSessionIdentity session, int expectedCurrent, int target, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill switching requires a transaction.");
        if (target < 0 || !sessions.IsInWorld(session) || onlineCharacters.StandingOf(session) is null)
            return null;
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false);
        if (view is null || view.World.Channel.PvpSkillProfileApplies) return null;
        var builds = await state.BuildsAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (builds.Active.Value != expectedCurrent || !builds.IsUnlocked(new(target))) return null;
        await state.ActivateAsync(session.CharacterId, new(target), ct).ConfigureAwait(false);
        return target;
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
        if (mode != 0 || selected.World.Channel.PvpSkillProfileApplies || onlineCharacters.StandingOf(session) is null)
            return new(CharacterSkillLearningRejection.UnsupportedScenario);
        if (pageIndex < 0)
            return new(CharacterSkillLearningRejection.PageUnavailable);
        var build = new SkillBuildId(pageIndex);
        if ((await state.BuildsAsync(session.CharacterId, ct).ConfigureAwait(false)).Active != build)
            return new(CharacterSkillLearningRejection.PageUnavailable);
        return await skillStore.Get(session.CharacterId, SkillGroup.Pve, build)
            .LearnAsync(session, changes, footprint, ct).ConfigureAwait(false);
    }
}
