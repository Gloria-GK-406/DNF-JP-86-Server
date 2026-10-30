using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Stands in for placement in tests whose subject is something else.
/// </summary>
internal sealed class StubEntryPlacementRuleService(ChannelEntryPolicyState? state = null, Func<int, bool>? innPredicate = null)
    : IEntryPlacementRuleService
{
    public ValueTask<bool> CanRememberInnAsync(int townId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(innPredicate?.Invoke(townId) ?? (townId > 0 && townId != 17));

    /// <summary>A plain arrival at the default town's gate.</summary>
    public static ChannelEntryPolicyState DefaultPlacement { get; } = new(
        ChannelId: 11,
        Spawn: new EntrySpawnState(1, 0, 474, 234, 5, 3, IsTransient: false),
        PersistsPosition: true,
        PvpSkillProfileApplies: false);

    public ValueTask<ChannelEntryPolicyState> ResolveAsync(
        ChannelEntryIdentity channel,
        int persistedTownId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(state ?? DefaultPlacement);

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel,
        int townId,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);

    public ValueTask<bool> DeclaresAreaAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken) => ValueTask.FromResult(townId > 0 && areaId >= 0);

    public ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public bool IsHouseholdTown(int townId) => Dfo.Server.Application.World.CoupleRoomRules.IsHouseholdTown(townId);

    public int? AreaInstance(int townId, int? householdInstance) =>
        Dfo.Server.Application.World.CoupleRoomRules.AreaInstance(townId, householdInstance);

    public TownCrossingRefusal? RefuseCrossing(TownCrossingFacts facts) =>
        Dfo.Server.Application.World.CoupleRoomRules.RefuseCrossing(facts);

    public Dfo.Server.Data.Characters.CharacterPositionState StoredPositionAfter(
        Dfo.Server.Data.Characters.CharacterPositionState standing, Dfo.Server.Data.Characters.CharacterPositionState destination) =>
        Dfo.Server.Application.World.CoupleRoomRules.StoredPositionAfter(standing, destination);

    public TownHousingChange HousingChangeOf(int fromTownId, int toTownId) =>
        Dfo.Server.Application.World.CoupleRoomRules.HousingChangeOf(fromTownId, toTownId);
}

/// <summary>Placement that cannot resolve, for proving entry fails before publication.</summary>
internal sealed class FailingEntryPlacementRuleService : IEntryPlacementRuleService
{
    public ValueTask<bool> CanRememberInnAsync(int townId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    public ValueTask<ChannelEntryPolicyState> ResolveAsync(
        ChannelEntryIdentity channel,
        int persistedTownId,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<ChannelEntryPolicyState>(
            new EntryPlacementUnavailableException("no arrival point is defined"));

    public ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel,
        int townId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<bool> DeclaresAreaAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<bool> SharesTownPresenceAsync(
        int townId,
        int areaId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
    public bool IsHouseholdTown(int townId) => Dfo.Server.Application.World.CoupleRoomRules.IsHouseholdTown(townId);

    public int? AreaInstance(int townId, int? householdInstance) =>
        Dfo.Server.Application.World.CoupleRoomRules.AreaInstance(townId, householdInstance);

    public TownCrossingRefusal? RefuseCrossing(TownCrossingFacts facts) =>
        Dfo.Server.Application.World.CoupleRoomRules.RefuseCrossing(facts);

    public Dfo.Server.Data.Characters.CharacterPositionState StoredPositionAfter(
        Dfo.Server.Data.Characters.CharacterPositionState standing, Dfo.Server.Data.Characters.CharacterPositionState destination) =>
        Dfo.Server.Application.World.CoupleRoomRules.StoredPositionAfter(standing, destination);

    public TownHousingChange HousingChangeOf(int fromTownId, int toTownId) =>
        Dfo.Server.Application.World.CoupleRoomRules.HousingChangeOf(fromTownId, toTownId);
}
