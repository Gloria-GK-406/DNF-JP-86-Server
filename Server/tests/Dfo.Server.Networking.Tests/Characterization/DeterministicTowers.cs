using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// Pins the one random input of a tower run: the run seed each tower's entry phase draws from
/// Random.Shared and hands to the tower.
/// </summary>
/// <remarks>
/// A determinism seam, like <see cref="DeterministicLayouts"/>. Every room, monster drop and clear
/// reward of a run follows from that seed, and it reaches content only through rule calls: the room
/// draw of each tower (map, actors, room seed, and the illusion tower's drops), the clear reward draw
/// of each tower, and the Boss tower's kill drops, which the independent drop rule draws from the run
/// seed itself. Only the seed argument of those calls is replaced. Decorating the tower domain services
/// instead would mean following every change to them, which is the code these transcripts pin.
/// The clear reward seed mixes in the member slot; replacing it gives every member the same draw.
/// </remarks>
internal static class DeterministicTowers
{
    public const uint RoomSeed = 0x5EED0001;
    public const ulong RewardSeed = 0x5EED0002;

    /// <summary>The Boss tower (BossTowerRuleService.DungeonId), the one tower whose kill drops use the run seed.</summary>
    private const int BossTowerDungeon = 3505;

    public static void Register(IServiceCollection services)
    {
        Replace<IBossTowerRuleService>(services, inner => new BossRooms(inner));
        Replace<IBossTowerRewardRuleService>(services, inner => new BossRewards(inner));
        Replace<IIllusionTowerRuleService>(services, inner => new IllusionRooms(inner));
        Replace<IIllusionTowerRewardRuleService>(services, inner => new IllusionRewards(inner));
        Replace<IIndependentDropRuleService>(services, inner => new BossTowerDrops(inner));
    }

    private static void Replace<TService>(IServiceCollection services, Func<TService, TService> decorate)
        where TService : class
    {
        var descriptor = services.Last(service => service.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.AddSingleton<TService>(provider => decorate(
            descriptor.ImplementationInstance as TService
                ?? (descriptor.ImplementationFactory is not null
                    ? (TService)descriptor.ImplementationFactory(provider)
                    : (TService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }

    private sealed class BossRooms(IBossTowerRuleService inner) : IBossTowerRuleService
    {
        public bool Supports(int dungeonId) => inner.Supports(dungeonId);
        public int EventId => inner.EventId;
        public int TicketItemId => inner.TicketItemId;
        public int PartyLimit => inner.PartyLimit;
        public TimeSpan LoadingTimeout => inner.LoadingTimeout;
        public TimeSpan ResultTimeout => inner.ResultTimeout;
        public bool CanTrade(int npcId, int shopId) => inner.CanTrade(npcId, shopId);
        public bool IsNextStageTarget(int currentStage, byte targetX, byte targetY) =>
            inner.IsNextStageTarget(currentStage, targetX, targetY);
        public EventConfiguration InitialActivity => inner.InitialActivity;
        public ValueTask<BossTowerDefinition> DefinitionAsync(CancellationToken ct) => inner.DefinitionAsync(ct);
        public ValueTask<BossTowerRoomSeed> CreateRoomAsync(int stage, uint seed, CancellationToken ct) =>
            inner.CreateRoomAsync(stage, RoomSeed, ct);
    }

    private sealed class BossTowerDrops(IIndependentDropRuleService inner) : IIndependentDropRuleService
    {
        public int Initialize() => inner.Initialize();
        public bool Covers(MonsterDropRequest request) => inner.Covers(request);
        public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent) =>
            inner.Draw(request.Context.DungeonId == BossTowerDungeon
                ? request with { Context = request.Context with { Seed = unchecked((int)RoomSeed) } }
                : request, playerLevel, growthBonusPercent);
    }

    private sealed class BossRewards(IBossTowerRewardRuleService inner) : IBossTowerRewardRuleService
    {
        public ValueTask<uint> KillExperienceAsync(int characterLevel, byte actorType, CancellationToken ct) =>
            inner.KillExperienceAsync(characterLevel, actorType, ct);
        public ValueTask<uint> ClearExperienceAsync(int characterLevel, int stage, CancellationToken ct) =>
            inner.ClearExperienceAsync(characterLevel, stage, ct);
        public ValueTask<ImmutableArray<TowerRewardCandidate>> CandidatesAsync(
            int characterLevel, int stage, ulong seed, CancellationToken ct) =>
            inner.CandidatesAsync(characterLevel, stage, RewardSeed, ct);
    }

    private sealed class IllusionRooms(IIllusionTowerRuleService inner) : IIllusionTowerRuleService
    {
        public bool Supports(int dungeonId) => inner.Supports(dungeonId);
        public ValueTask<IllusionTowerDefinition> GetDefinitionAsync(CancellationToken cancellationToken, int dungeonId) =>
            inner.GetDefinitionAsync(cancellationToken, dungeonId);
        public ValueTask<TowerRoomSeed> CreateRoomAsync(int level, uint seed, CancellationToken cancellationToken, int dungeonId) =>
            inner.CreateRoomAsync(level, RoomSeed, cancellationToken, dungeonId);
        public TimeSpan DefaultReturnDelay => inner.DefaultReturnDelay;
        public TimeSpan? ReturnDelay(byte option) => inner.ReturnDelay(option);
    }

    private sealed class IllusionRewards(IIllusionTowerRewardRuleService inner) : IIllusionTowerRewardRuleService
    {
        public ValueTask<uint> KillExperienceAsync(int characterLevel, byte actorType, CancellationToken cancellationToken, int dungeonId) =>
            inner.KillExperienceAsync(characterLevel, actorType, cancellationToken, dungeonId);
        public ValueTask<uint> ClearExperienceAsync(int characterLevel, int rewardLevel, CancellationToken cancellationToken, int dungeonId) =>
            inner.ClearExperienceAsync(characterLevel, rewardLevel, cancellationToken, dungeonId);
        public ValueTask<ImmutableArray<TowerRewardCandidate>> CandidatesAsync(int characterLevel, int rewardLevel,
            ulong seed, CancellationToken cancellationToken, int dungeonId) =>
            inner.CandidatesAsync(characterLevel, rewardLevel, RewardSeed, cancellationToken, dungeonId);
    }
}
