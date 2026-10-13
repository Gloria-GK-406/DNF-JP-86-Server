using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Settlement;

/// <summary>Applies one prepared quest decision within the caller's use case transaction.</summary>
public interface IQuestCompletionSettlementDomainService
{
    ValueTask<QuestCompletionReceipt> FinishAsync(AccountId account, CharacterId character, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, CancellationToken ct, SelectedSessionIdentity? session = null);
}
