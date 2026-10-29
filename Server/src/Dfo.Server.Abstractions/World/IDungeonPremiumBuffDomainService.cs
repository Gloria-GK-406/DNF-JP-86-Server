using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// The premium dungeon buff a character spends on a run: one per run, paid from the daily usage.
/// </summary>
/// <remarks>Runs inside the caller's write unit.</remarks>
public interface IDungeonPremiumBuffDomainService
{
    /// <returns>
    /// Whether this report spent the buff. A run that is no longer the character's current one, or
    /// one whose buff is already spent, spends nothing.
    /// </returns>
    ValueTask<bool> TryConsumeAsync(AccountId accountId, CharacterId characterId, DungeonRunId runId,
        CancellationToken cancellationToken);
}
