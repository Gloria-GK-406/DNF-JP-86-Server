using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Raids;

public enum AntonRaidRewardKind : byte
{
    Gold = 0,
    PartyCard = 1,
    SquadItem = 3,
    Quest = 4,
    RetryCertificate = 5,
}

public readonly record struct AntonRaidRewardKey(
    Guid Instance, byte Phase, CharacterId Character, AntonRaidRewardKind Kind);

/// <summary>Claims rewards in the same character write unit as their effects.</summary>
public interface IAntonRaidRewardLedger
{
    ValueTask<bool> TryClaimAsync(AntonRaidRewardKey key, CancellationToken cancellationToken);

    ValueTask<bool> TryCountQuestClearAsync(CharacterId character, int quest, byte phase,
        DateOnly period, CancellationToken cancellationToken);
}
