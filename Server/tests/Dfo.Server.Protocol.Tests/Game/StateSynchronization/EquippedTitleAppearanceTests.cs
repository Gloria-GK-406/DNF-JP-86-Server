using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class EquippedTitleAppearanceTests
{
    [Fact]
    public void Equipping_and_removing_a_title_changes_the_counted_appearance_without_changing_the_tail()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var entry = source.CharacterEntry.Public;
        var removed = StateSynchronizationPayloadCodec.EncodeCurrentUserState(source.Character, entry, [], []);
        var equipped = StateSynchronizationPayloadCodec.EncodeCurrentUserState(source.Character, entry,
            [new(null, 13, StackableItemCore.Create(1, 26628, 0))], []);

        using var reader = new BinaryReader(new MemoryStream(equipped));
        reader.BaseStream.Position = 41; // A21 subtype/version and reserved block.
        reader.ReadUInt16(); // Character id.
        reader.ReadBytes(reader.ReadInt32()); // Length-prefixed name.
        reader.ReadBytes(6); // Job, growth, level and public state.
        var countOffset = checked((int)reader.BaseStream.Position);
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(13, reader.ReadByte());
        Assert.Equal(26628, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(new byte[4], reader.ReadBytes(4));
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(0, reader.ReadByte());

        Assert.Equal(0, removed[countOffset]);
        Assert.Equal(removed.Length + 23, equipped.Length);
        Assert.Equal(removed[..countOffset], equipped[..countOffset]);
        Assert.Equal(removed[(countOffset + 1)..], equipped[checked((int)reader.BaseStream.Position)..]);
    }
}
