using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

internal sealed class ExpertStoreGameplayDomainService(
    IExpertStoreDomainService stores, ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
    ExpertStorePlacementRuleService placementRules, IExpertJobQueryDomainService jobs, IExpertJobRuleService rules, IPartyService parties,
    IPrivateStoreQueryDomainService privateStores, IOnlineCharacterRuntimeDomainService onlineCharacters) : IExpertStoreGameplayDomainService
{
    public async ValueTask<ExpertStoreResult> OpenAsync(SelectedSessionIdentity owner, ExpertStorePlacement placement, CancellationToken ct)
    {
        if (placement.Name.IsDefault || placement.Name.Length > byte.MaxValue || placement.Cost < 0 || !Enum.IsDefined(placement.Kind))
            return new(ExpertStoreRejection.InvalidRequest);
        var standing = Standing(owner);
        if (standing is null || standing.Area.TownId == 0 || parties.KeyOf(owner.CharacterId).Kind == PartyKeyKind.Real
            || privateStores.Find(owner.CharacterId) is not null || stores.HasOpenStore(owner.CharacterId))
            return new(ExpertStoreRejection.InvalidState);

        if (await MachineAsync(owner, placement.Kind, ct).ConfigureAwait(false) is not { } machine
            || placement.Cost > machine.Definition.MaximumStoreCharge)
            return new(ExpertStoreRejection.InvalidState);
        var location = await placementRules.CheckAsync(standing.Area, placement.X, placement.Y, ct).ConfigureAwait(false);
        if (location != ExpertStoreRejection.None)
            return new(location);

        if (Standing(owner)?.Area != standing.Area || stores.TryOpen(owner, standing.Area, placement) is not { } store)
            return new(ExpertStoreRejection.InvalidState);
        _ = onlineCharacters.StepTo(owner, placement.X, placement.Y, standing.Position.Direction);
        return new(ExpertStoreRejection.None, ViewOf(store, machine));
    }

    public async ValueTask<ExpertStoreResult> EnterAsync(SelectedSessionIdentity visitor, ushort ownerUserId, CancellationToken ct)
    {
        if (ownerUserId == 0) return new(ExpertStoreRejection.InvalidRequest);
        if (Standing(visitor) is not { } standing)
            return new(ExpertStoreRejection.InvalidState);
        var store = stores.In(standing.Area).SingleOrDefault(candidate => candidate.Owner.WireUserId == ownerUserId);
        if (store is null || !CanVisit(visitor, store)
            || await MachineAsync(store.Owner, store.Kind, ct).ConfigureAwait(false) is not { } machine
            || !CanVisit(visitor, store) || !stores.Enter(visitor, store))
            return new(ExpertStoreRejection.InvalidState);
        return new(ExpertStoreRejection.None, ViewOf(store, machine));
    }

    public async ValueTask<IReadOnlyList<ExpertStoreView>> VisibleToAsync(SelectedSessionIdentity viewer, CancellationToken ct)
    {
        if (Standing(viewer) is not { } standing) return [];
        var visible = new List<ExpertStoreView>();
        foreach (var store in stores.In(standing.Area))
            if (store.Owner != viewer && CanVisit(viewer, store)
                && await MachineAsync(store.Owner, store.Kind, ct).ConfigureAwait(false) is { } machine
                && CanVisit(viewer, store) && ReferenceEquals(stores.FindOwned(store.Owner), store))
                visible.Add(ViewOf(store, machine));
        return visible;
    }

    public ExpertStoreVisit? FindUse(SelectedSessionIdentity visitor, ushort ownerUserId, ExpertStoreKind kind) =>
        stores.Visiting(visitor) is { } visit && visit.Store.Owner.WireUserId == ownerUserId
            && visit.Store.Kind == kind && CanVisit(visitor, visit.Store) ? visit : null;

    public async ValueTask<ExpertJobState?> ValidateUseAsync(ExpertStoreVisit visit, CancellationToken ct)
    {
        if (FindUse(visit.Visitor, visit.Store.Owner.WireUserId, visit.Store.Kind) is not { } current
            || !ReferenceEquals(current.Store, visit.Store))
            return null;
        var machine = await MachineAsync(visit.Store.Owner, visit.Store.Kind, ct).ConfigureAwait(false);
        return ReferenceEquals(stores.Visiting(visit.Visitor)?.Store, visit.Store) && CanVisit(visit.Visitor, visit.Store)
            ? machine?.State : null;
    }

    private TownAreaMember? Standing(SelectedSessionIdentity session) =>
        sessions.IsInWorld(session) && townAreas.Find(session.CharacterId) is { SharesTownPresence: true } member
            && member.Session == session ? member : null;

    private bool CanVisit(SelectedSessionIdentity visitor, ExpertStoreSnapshot store) =>
        Standing(visitor)?.Area == store.Area && Standing(store.Owner)?.Area == store.Area;

    private async ValueTask<Machine?> MachineAsync(SelectedSessionIdentity owner, ExpertStoreKind kind, CancellationToken ct)
    {
        var state = await jobs.ReadAsync(owner.CharacterId, ct).ConfigureAwait(false);
        var eligible = kind switch
        {
            ExpertStoreKind.DisjointMachine => state.Type == ExpertJobType.Disjointer
                && state.DisjointMachineGrade is > 0 and <= byte.MaxValue && state.DisjointMachineEndurance > 0,
            ExpertStoreKind.EnchantShop => state.Type == ExpertJobType.Enchanter && state.EnchanterEndurance > 0,
            _ => false,
        };
        return eligible && await rules.FindAsync(state.Type, ct).ConfigureAwait(false) is { } definition
            ? new(state, definition) : null;
    }

    private static ExpertStoreView ViewOf(ExpertStoreSnapshot store, Machine machine) =>
        store.Kind == ExpertStoreKind.DisjointMachine
            ? new(store, machine.State.DisjointMachineGrade, machine.State.DisjointMachineEndurance, [])
            : new(store, 0, machine.State.EnchanterEndurance, machine.Definition.CardQualificationsAt(machine.State.Experience));

    private sealed record Machine(ExpertJobState State, ExpertJobDefinition Definition);
}
