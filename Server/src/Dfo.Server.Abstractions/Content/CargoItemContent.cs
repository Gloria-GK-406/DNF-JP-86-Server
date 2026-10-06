namespace Dfo.Server.Abstractions.Content;

public sealed record CargoItemDefinition(string AttachType, IReadOnlySet<string> ImpossibleContents);

public interface ICargoItemDefinitionSource
{
    ValueTask<CargoItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
