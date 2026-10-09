using Dfo.Server.Application.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEpicBookDefinitionSourceTests
{
    [SkippableFact]
    public void Real_archive_maps_piece_ids_to_equipment_without_a_hardcoded_catalog()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rules = new EpicBookRuleService(new PvfEpicBookDefinitionSource(provider));
        Assert.Equal(710, rules.Catalog().Count);
        Assert.Equal(450004, rules.FindPiece(10300000)!.EquipmentId);
        Assert.Null(rules.FindPiece(3330));
        Assert.Equal(710, rules.Catalog().Select(x => x.PieceId).Distinct().Count());
        Assert.All(new PvfHellRewardDefinitionSource(provider).Load().EpicPieces,
            pair => Assert.Equal(pair.Key, rules.FindPiece(pair.Value)!.EquipmentId));
    }
}
