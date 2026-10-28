using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>Only the native quest 8557 crystal exchange's daily quota.</summary>
public interface IGiantDragonCrystalExchangeDomainService
{
    public const int QuestId = 8557;
    ValueTask<bool> CanAcceptAsync(CharacterId character, CancellationToken ct);
    ValueTask<bool> TryCompleteAsync(CharacterId character, CancellationToken ct);
}
