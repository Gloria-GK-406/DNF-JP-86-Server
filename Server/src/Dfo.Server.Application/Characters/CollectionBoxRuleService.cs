using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

internal sealed class CollectionBoxRuleService(ICollectionBoxDefinitionSource definitions) : ICollectionBoxRuleService
{
    public async ValueTask<IReadOnlyList<CollectionBoxDefinition>> CatalogAsync(CancellationToken ct)
    {
        var boxes = await definitions.GetAllAsync(ct).ConfigureAwait(false);
        if (boxes.Count > 255 || boxes.Select(x => x.BoxIndex).Distinct().Count() != boxes.Count)
            throw new InvalidDataException("Collection box IDs must be unique bytes.");
        foreach (var box in boxes)
        {
            if (box.BoxIndex is < 1 or > 255 || box.Columns < 1 || box.Rows < 1
                || (long)box.Columns * box.Rows > 256 || box.Slots.Count != (long)box.Columns * box.Rows
                || box.Slots.Any(x => x.Column < 0 || x.Column >= box.Columns || x.Row < 0 || x.Row >= box.Rows
                    || x.ItemTemplateId is 0 or < -1)
                || box.Slots.Select(x => (x.Column, x.Row)).Distinct().Count() != box.Slots.Count
                || box.Slots.Where(x => x.ItemTemplateId > 0).GroupBy(x => x.ItemTemplateId).Any(x => x.Count() > 1)
                || box.Slots.Count(x => x.ItemTemplateId > 0) > 255)
                throw new InvalidDataException($"Invalid collection box definition {box.BoxIndex}.");
        }
        // Snapshot the catalog so a caller never mixes two generations of mutable definitions.
        return boxes.OrderBy(x => x.BoxIndex).Select(x => x with { Slots = x.Slots.ToArray(), Extensions = x.Extensions.ToArray() }).ToArray();
    }
}
