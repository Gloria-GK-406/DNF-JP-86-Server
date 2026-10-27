using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

/// <summary>A guarded housing write refused after mutations; the command must roll back before replying.</summary>
public sealed class HousingRejectedException(int status) : Exception($"Housing operation refused: {status}.")
{ public int Status { get; } = status; }

public interface IMarriageHousingSessionDomainService
{
    void CloseExact(SelectedSessionIdentity actor);
    void RememberReturn(SelectedSessionIdentity actor, Dfo.Server.Data.Characters.CharacterPositionState position);
    Dfo.Server.Data.Characters.CharacterPositionState? ReturnFor(SelectedSessionIdentity actor);
}

public interface IMarriageHousingDomainStore
{
    ValueTask<HousingSnapshot?> ReadAsync(Guid relation, CancellationToken ct);
    ValueTask<HousingSnapshot> EnsureAsync(Guid relation, CancellationToken ct);
    ValueTask SaveAsync(HousingSnapshot snapshot, CancellationToken ct);
    ValueTask<bool> RecordHarvestAsync(Guid planting, int round, IReadOnlyList<HousingReward> rewards, CancellationToken ct);
}
public interface IMarriageHousingQueryDomainService
{
    ValueTask<HousingSnapshot?> ReadAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<MarriageInfoSnapshot?> InfoAsync(AccountId account, CharacterId character, CancellationToken ct);
    bool IsEntrySource(byte town, byte area);
    ValueTask<int?> InstanceAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<int> PointsAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<byte> RingAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<HousingBenefits> BenefitsAsync(AccountId account, CharacterId character, bool pvp, CancellationToken ct);
}
public interface IMarriageHousingDomainService
{
    ValueTask<HousingResult> ExecuteAsync(SelectedSessionIdentity actor, HousingRequest request, IReadOnlyList<AccountId> accounts, IEffectFootprintRecorder footprint, CancellationToken ct);
    ValueTask InitializeAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<Dfo.Server.Data.Inventory.NpcShopPurchaseResult> PurchaseAsync(SelectedSessionIdentity actor,
        int itemId, int count, int shopId, IReadOnlyList<AccountId> accounts, CancellationToken ct);
}
