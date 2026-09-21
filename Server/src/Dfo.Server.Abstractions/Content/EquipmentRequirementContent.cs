namespace Dfo.Server.Abstractions.Content;
public interface IEquipmentRequirementDefinitionSource
{
    ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken);
}
