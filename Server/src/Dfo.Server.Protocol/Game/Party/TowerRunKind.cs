using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.BossTowerContinue;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>
/// One tower engine as the projections both towers share see it. Each member answers a point where
/// the Boss tower and the illusion tower really differ; everything else is written once against
/// <see cref="ITowerRunSnapshot"/>.
/// </summary>
internal interface ITowerRunKind
{
    /// <summary>The engine as log text, e.g. "Boss tower".</summary>
    string Name { get; }

    ITowerRunSnapshot? Find(SelectedSessionIdentity session);
    bool IsInTower(CharacterId characterId);

    ValueTask<ITowerActionOutcome> LeaveAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken token);
    ValueTask<ITowerActionOutcome> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken token);
    ValueTask<ITowerActionOutcome> StartAsync(TowerActionContext context, CancellationToken token);

    /// <summary>The engine's part of a party work unit's name, e.g. party-leave-boss-tower.</summary>
    string WorkLabel { get; }

    /// <summary>
    /// Whether the engine's party work units name the run's members as recipients. The Boss tower
    /// does; the illusion tower declares none and relies on its footprint owners alone.
    /// </summary>
    bool NamesWorkRecipients { get; }

    /// <summary>
    /// Whether a party departure starts a room every remaining member has loaded. The Boss tower
    /// starts it and announces the fight; the illusion tower only repeats the loading answer.
    /// </summary>
    bool StartsReadyRoomOnDeparture { get; }

    /// <summary>Whether returning to town must restore the member's own inventory first.</summary>
    bool RestoresInventoryOnReturn(ITowerRunSnapshot run);

    /// <summary>The engine's own frames that close a member's settlement: ranking, reward cards, continue.</summary>
    ValueTask AppendSettlementResultAsync(List<IGameServerMessage> frames, ITowerRunSnapshot run,
        TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt, SelectedSessionIdentity session,
        CancellationToken token);
}

internal sealed class BossTowerRunKind(IBossTowerDomainService towers) : ITowerRunKind
{
    /// <summary>The Boss tower shows eight reward card slots.</summary>
    private const byte RewardSlots = 8;

    public string Name => "Boss tower";
    public ITowerRunSnapshot? Find(SelectedSessionIdentity session) => towers.Find(session);
    public bool IsInTower(CharacterId characterId) => towers.IsInTower(characterId);

    public async ValueTask<ITowerActionOutcome> LeaveAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken token) =>
        await towers.LeaveAsync(context, false, footprint, token).ConfigureAwait(false);

    public async ValueTask<ITowerActionOutcome> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken token) =>
        await towers.SynchronizeLeaderAsync(context, leader, token).ConfigureAwait(false);

    public async ValueTask<ITowerActionOutcome> StartAsync(TowerActionContext context, CancellationToken token) =>
        await towers.StartAsync(context, token).ConfigureAwait(false);

    public string WorkLabel => "boss";
    public bool NamesWorkRecipients => true;
    public bool StartsReadyRoomOnDeparture => true;
    public bool RestoresInventoryOnReturn(ITowerRunSnapshot run) => false;

    public async ValueTask AppendSettlementResultAsync(List<IGameServerMessage> frames, ITowerRunSnapshot run,
        TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt, SelectedSessionIdentity session,
        CancellationToken token)
    {
        frames.Add(new BossTowerRewardNotification(member.Experience,
            receipt.Members.Where(value => value.Slot < RewardSlots)
                .ToDictionary(value => value.Slot, value => value.Candidates)));
        frames.Add(new BossTowerContinueNotification(
            await towers.CanRetryAsync(TowerActionProjection.Context(run, session), token).ConfigureAwait(false)));
    }
}

internal sealed class IllusionTowerRunKind(IIllusionTowerDomainService towers) : ITowerRunKind
{
    /// <summary>The illusion tower shows four reward card slots.</summary>
    private const byte RewardSlots = 4;

    public string Name => "Illusion tower";
    public ITowerRunSnapshot? Find(SelectedSessionIdentity session) => towers.Find(session);
    public bool IsInTower(CharacterId characterId) => towers.IsInTower(characterId);

    public async ValueTask<ITowerActionOutcome> LeaveAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken token) =>
        await towers.LeaveAsync(context, false, footprint, token).ConfigureAwait(false);

    public async ValueTask<ITowerActionOutcome> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken token) =>
        await towers.SynchronizeLeaderAsync(context, leader, token).ConfigureAwait(false);

    public async ValueTask<ITowerActionOutcome> StartAsync(TowerActionContext context, CancellationToken token) =>
        await towers.StartAsync(context, token).ConfigureAwait(false);

    public string WorkLabel => "illusion";
    public bool NamesWorkRecipients => false;
    public bool StartsReadyRoomOnDeparture => false;
    public bool RestoresInventoryOnReturn(ITowerRunSnapshot run) => run is IllusionTowerSnapshot { UsesTowerInventory: true };

    public async ValueTask AppendSettlementResultAsync(List<IGameServerMessage> frames, ITowerRunSnapshot run,
        TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt, SelectedSessionIdentity session,
        CancellationToken token)
    {
        var completion = run.Completion!;
        var elapsed = (int)Math.Clamp((completion.CompletedAt - run.StartedAt).TotalMilliseconds, 0, int.MaxValue);
        frames.Add(new IllusionTowerRankingNotification(run.DungeonId, completion.CompletedLevels, elapsed));
        frames.Add(new IllusionTowerRewardNotification(member.Experience,
            receipt.Members.Where(value => value.Slot < RewardSlots)
                .ToDictionary(value => value.Slot, value => value.Candidates)));
        frames.Add(new IllusionTowerContinueNotification(
            await towers.CanContinueAsync(TowerActionProjection.Context(run, session), token).ConfigureAwait(false)));
    }
}
