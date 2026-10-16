using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

public interface IMarriageChurchDomainStore
{
    ValueTask<MarriageChurchRoom?> ReadAsync(Guid ceremonyId, CancellationToken ct);
    ValueTask<MarriageChurchRoom?> TryCreateAsync(Guid ceremonyId, int channelId,
        MarriageChurchLocation location, DateTimeOffset at, DateTimeOffset until, CancellationToken ct);
    ValueTask RenewAsync(MarriageChurchRoom room, DateTimeOffset at, DateTimeOffset until, CancellationToken ct);
    ValueTask AdmitAsync(Guid ceremonyId, CharacterId character, CancellationToken ct);
    ValueTask<bool> IsAdmittedAsync(Guid ceremonyId, CharacterId character, CancellationToken ct);
}

public interface IMarriageChurchRuleService
{
    ValueTask<bool> IsChurchAsync(byte townId, byte areaId, CancellationToken ct);
    ValueTask<IReadOnlyList<MarriageChurchLocation>> LocationsAsync(byte townId, CancellationToken ct);
    TimeSpan PreparationTime(int waitIndex);
}

public interface IMarriageChurchDomainService
{
    ValueTask<MarriageChurchSnapshot?> PrepareAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<MarriageChurchEntryResult> EnterAsync(SelectedSessionIdentity actor,
        uint peer, uint churchArea, byte flag, CancellationToken ct);
    ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channelId,
        byte townId, byte areaId, CancellationToken ct);
}
