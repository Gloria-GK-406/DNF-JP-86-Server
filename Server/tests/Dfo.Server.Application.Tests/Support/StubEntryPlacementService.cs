using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Stands in for placement in tests whose subject is something else.
/// </summary>
internal sealed class StubEntryPlacementService(ChannelEntryPolicyState? state = null)
    : IEntryPlacementService
{
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
}

/// <summary>Placement that cannot resolve, for proving entry fails before publication.</summary>
internal sealed class FailingEntryPlacementService : IEntryPlacementService
{
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
}
