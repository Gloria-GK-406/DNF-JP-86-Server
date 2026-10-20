using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfSouthernDaleContentSource(PvfArchiveProvider provider, IDungeonDefinitionSource dungeons)
    : ISouthernDaleContentSource
{
    public async ValueTask<SouthernDaleDefinition> ReadAsync(CancellationToken ct)
    {
        var dungeon = await dungeons.GetAsync(100, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing Southern Dale dungeon.");
        var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, dungeon.ArchivePath);
        IReadOnlyList<int> Read(string name)
        {
            var matches = sections.Where(x => x.Name == name).ToArray();
            if (matches.Length != 1 || matches[0].Values.Any(x => x.Kind != PvfTokenKind.Int32))
                throw new InvalidDataException($"Missing or invalid Southern Dale {name}.");
            return matches[0].Values.Select(x => x.Value).ToArray();
        }
        var party = Read("[limit party count]");
        if (party.Count != 1 || party[0] is < 1 or > 4 || dungeon.ConsumesFatigue || dungeon.OffersPaidCard
            || !Read("[prohibit practice]").SequenceEqual([1])
            || Read("[no giveup panalty]").Count != 0
            || !Read("[schedule]").SequenceEqual([2330])
            || dungeon.RequiredItems.Count != 1 || dungeon.RequiredItems[0] != new DungeonRequiredItem(2680738, 1, true))
            throw new InvalidDataException("Unsupported Southern Dale admission declaration.");
        var rewards = Read("[clear reward item]");
        if (rewards.Count == 0 || rewards.Count % 2 != 0 || rewards.Any(x => x <= 0))
            throw new InvalidDataException("Invalid Southern Dale clear rewards.");
        return new(dungeon, party[0], Enumerable.Range(0, rewards.Count / 2)
            .Select(i => new ItemDeliveryGrant(rewards[2 * i], rewards[2 * i + 1])).ToArray());
    }
}
