using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Support;

internal sealed class TowerDomainStub(params SelectedSessionIdentity[] members) : IIllusionTowerDomainService
{
    public IllusionTowerSnapshot Snapshot { get; } = new(
        Guid.NewGuid(),
        11001,
        [.. members.Select((session, index) => new TowerMember(session, checked((byte)index), index == 0, false))],
        new TowerRoomSnapshot(1, 1001, 42, 1, TowerRoomPhase.Fighting, [], [], [], DateTimeOffset.UnixEpoch),
        [],
        null,
        DateTimeOffset.UnixEpoch);

    public IllusionTowerSnapshot? Find(SelectedSessionIdentity session) =>
        Snapshot.Members.Any(member => member.Session == session) ? Snapshot : null;

    public bool IsInTower(CharacterId characterId) =>
        Snapshot.Members.Any(member => member.Session.CharacterId == characterId);

    public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members) => false;
    public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
        SelectedSessionIdentity leader, uint seed, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> DieAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context,
        SelectedSessionIdentity leader, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken cancellationToken) => throw new NotSupportedException();
    public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now) => [];
    public ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
