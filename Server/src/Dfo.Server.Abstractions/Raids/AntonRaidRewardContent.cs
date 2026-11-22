namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidRewardItem(int ItemId, int Quantity);

public sealed record AntonRaidRewardChoice(int Weight, AntonRaidRewardItem Item);

public sealed record AntonRaidRewardPool(AntonRaidRewardKind Kind, int State, int Weight, byte Flags,
    IReadOnlyList<AntonRaidRewardChoice> Choices);

public sealed record AntonRaidRankRange(int Minimum, int Maximum, byte Rank);

public sealed record AntonRaidPhaseRewardDefinition(IReadOnlyList<AntonRaidRankRange> Ranks,
    IReadOnlyList<AntonRaidRewardPool> Pools);

public sealed record AntonRaidRewardDefinition(IReadOnlyList<AntonRaidPhaseRewardDefinition> Phases,
    AntonRaidRewardItem RetryCertificate);

public interface IAntonRaidRewardContentSource
{
    AntonRaidRewardDefinition Read();
}

/// <summary>A single recipient's verified raid phase clear, published from the quest grant.</summary>
public sealed record AntonRaidAchievementClear(Dfo.Server.Data.Characters.CharacterId Character,
    byte Phase, byte Role) : Dfo.Server.Abstractions.DomainEvents.IDomainEvent;
