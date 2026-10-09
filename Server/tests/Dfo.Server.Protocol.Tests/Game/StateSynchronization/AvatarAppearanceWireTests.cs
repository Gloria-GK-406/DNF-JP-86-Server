using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class AvatarAppearanceWireTests
{
    [Theory]
    [InlineData(null, 321)]
    [InlineData(0, 0)]
    [InlineData(42609, 0)]
    [InlineData(40735, 40735)]
    public void Full_equipment_keeps_real_template_and_projects_canonical_appearance_over_legacy(int? appearance, int expected)
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var core = StackableItemCore.Create(8, 42609, 1234) with { AppearanceItemId = appearance };
        var body = StateSynchronizationPayloadCodec.EncodeExtendedUserState(source.Character,
            source.CharacterEntry.Public, source.CharacterEntry.Skills,
            [new(new(987), 3, core, new(0, 321, new byte[30], 17, 29, 0))],
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var needle = new byte[5]; needle[0] = 3; BinaryPrimitives.WriteInt32LittleEndian(needle.AsSpan(1), 42609);
        var offset = body.AsSpan().IndexOf(needle); Assert.True(offset >= 0);
        Assert.Equal(expected, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset + 12)));
    }
}
