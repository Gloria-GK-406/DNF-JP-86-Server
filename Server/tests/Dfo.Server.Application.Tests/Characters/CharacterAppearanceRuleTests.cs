using System.Buffers.Binary;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Characters;

[Trait("Category", "Unit")]
public sealed class CharacterAppearanceRuleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 8)]
    [InlineData(3, 10)]
    [InlineData(4, 16)]
    [InlineData(5, 18)]
    [InlineData(6, 24)]
    [InlineData(7, 26)]
    public void Explicit_facts_resolve_every_display_combination_and_keep_equipment_and_unknown_bits(int mask, int bits)
    {
        var blob = new byte[150];
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2), (ushort)((mask & 1) != 0 ? 1 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(110), (ushort)((mask & 2) != 0 ? 0 : 1));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(148), (ushort)((mask & 4) != 0 ? 0 : 1));
        var source = StackableItemCore.Create(1, 10400, 1) with { AppearanceItemId = 10401 };
        var rule = CharacterAppearanceRuleService.Shared;
        var display = rule.Resolve(new(new(0, 1, 2)), [new(null, 13, source)], new(blob, 0xA5));
        Assert.Equal((byte)(0xA5 | bits), display.VisibilityBits);
        Assert.Equal((mask & 4) != 0 ? new CharacterCareer(0) : new CharacterCareer(0, 1, 2), display.NativeAvatar);
        Assert.Equal(10401u, display.TitleAppearanceItemId);
        Assert.Equal(10400, Assert.Single(display.Parts).DisplayItemId);
        Assert.Equal(10401, source.AppearanceItemId);
        Assert.Equal(0x21, display.Avatars.Identity.PackedGrowType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(33)]
    public void Disabling_native_career_avatar_selects_default_without_changing_actual_career(byte packed)
    {
        var display = CharacterAppearanceRuleService.Shared.Resolve(
            new(CharacterCareer.FromPacked(4, packed)), [], new(new byte[150], 3));
        Assert.Equal(new CharacterCareer(4), display.NativeAvatar);
        Assert.Equal(packed, display.Avatars.Identity.PackedGrowType);
    }
}
