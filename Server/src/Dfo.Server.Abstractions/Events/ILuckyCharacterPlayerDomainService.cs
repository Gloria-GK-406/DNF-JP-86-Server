using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Events;

public enum LuckyCharacterBurningError
{
    None,
    OutsideTown,
    InsufficientFatigue,
    Ineligible,
}

public readonly record struct LuckyCharacterBurningResult(LuckyCharacterBurningError Error, bool? Enabled = null);

public interface ILuckyCharacterPlayerDomainService
{
    ValueTask<LuckyCharacterPeriod> ReadRosterAsync(int listenerPort, CancellationToken ct);
    ValueTask<LuckyCharacterBurningResult> SetBurningAsync(SelectedSessionIdentity session, bool enabled, CancellationToken ct);
}
