using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Administration;

public sealed class GmMailEquipmentOptionsTests
{
    // Normalize and PayloadHash are pure; the content dependencies are never touched.
    private static readonly GmMailRuleService Rules = new(null!, null!, null!, null!);

    private static GmMailInput Input(params GmMailItem[] items) => new("title", "body", [1], items, 0);

    [Fact]
    public void Pre_extension_option_json_and_payload_hash_stay_byte_identical()
    {
        const string legacy = """{"Title":"title","Body":"body","RecipientIds":[1],"Items":[{"ItemId":27312,"Count":1,"Equipment":{"Seal":"unsealed","Growth":"reinforcement","Level":7,"AmplifyType":0}}],"Gold":0}""";
        var input = Rules.Normalize(Input(new GmMailItem(27312, 1, new("unsealed", "reinforcement", 7))), true);
        Assert.Equal(legacy, JsonSerializer.Serialize(input));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy))), Rules.PayloadHash(input));
        var stored = JsonSerializer.Deserialize<GmMailInput>(legacy)!;
        Assert.Equal(Rules.PayloadHash(input), Rules.PayloadHash(Rules.Normalize(stored, true)));
    }

    [Fact]
    public void Extended_options_serialize_only_when_set_and_highest_tier_is_the_default()
    {
        Assert.Equal("""{"Seal":"default","Growth":"none","Level":0,"AmplifyType":0,"Forge":3,"Socket":true,"Quality":0}""",
            JsonSerializer.Serialize(new ItemEquipmentOptions(Forge: 3, Socket: true, Quality: 0)));
        Assert.Null(Rules.Normalize(Input(new GmMailItem(27312, 1, new(Quality: 4))), true).Items[0].Equipment);
        Assert.Equal(2, Rules.Normalize(Input(new GmMailItem(27312, 1, new(Quality: 2))), true).Items[0].Equipment!.Quality);
        Assert.Equal(new ItemEquipmentOptions(Forge: 5), Rules.Normalize(Input(new GmMailItem(27312, 1, new(Forge: 5, Quality: 4))), true).Items[0].Equipment);
        var exception = Assert.Throws<GmMailException>(() => Rules.Normalize(Input(new GmMailItem(27312, 1, new(Quality: 4)), new GmMailItem(27312, 1)), true));
        Assert.Equal(GmMailRefusal.Invalid, exception.Reason);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(-1, 0)]
    [InlineData(null, -1)]
    [InlineData(null, 256)]
    public void Out_of_range_quality_or_forge_is_an_invalid_request(int? quality, int forge)
    {
        var exception = Assert.Throws<GmMailException>(() => Rules.Normalize(Input(new GmMailItem(27312, 1, new(Forge: forge, Quality: quality))), true));
        Assert.Equal(GmMailRefusal.Invalid, exception.Reason);
    }

    [Fact]
    public void Each_tier_maps_to_its_fixed_A21_attribute_seed()
    {
        Assert.Equal([999999993, 999999996, 999999999, 999999995, 999999998], Enumerable.Range(0, 5).Select(EquipmentQualitySeeds.FixedFor));
        Assert.All(Enumerable.Range(0, 5), tier => Assert.True(EquipmentQualitySeeds.IsFixed(EquipmentQualitySeeds.FixedFor(tier))));
        Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentQualitySeeds.FixedFor(5));
    }

    [Fact]
    public void Socket_class_names_shared_or_mixed_layouts()
    {
        Assert.Equal("C", ItemEquipmentRuleService.SocketClass([4, 4]));
        Assert.Equal("A/B", ItemEquipmentRuleService.SocketClass([1, 2]));
        Assert.Equal("S", ItemEquipmentRuleService.SocketClass([16]));
        Assert.Null(ItemEquipmentRuleService.SocketClass(ImmutableArray<ushort>.Empty));
    }
}
