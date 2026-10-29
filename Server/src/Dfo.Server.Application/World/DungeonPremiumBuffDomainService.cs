using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonPremiumBuffDomainService(IDungeonRunDomainService runs,
    IPremiumServiceUsageDomainService usage) : IDungeonPremiumBuffDomainService
{
    public async ValueTask<bool> TryConsumeAsync(AccountId accountId, CharacterId characterId, DungeonRunId runId,
        CancellationToken cancellationToken)
    {
        if (runs.Find(characterId)?.RunId != runId
            || runs.HasUsedDungeonBuff(characterId, runId)
            || !await usage.TryUseDungeonBuffAsync(accountId, characterId, cancellationToken).ConfigureAwait(false))
            return false;
        if (!runs.StageDungeonBuff(characterId, runId))
            throw new InvalidOperationException("Dungeon changed during buff consumption.");
        return true;
    }
}
