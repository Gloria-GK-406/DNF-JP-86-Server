using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class ClientAppearanceOptionsTests
{
    [Theory]
    [InlineData(false, false, false, 25)]
    [InlineData(true, false, false, 27)]
    [InlineData(false, true, false, 17)]
    [InlineData(false, false, true, 9)]
    [InlineData(true, true, true, 3)]
    public void Verified_u16_slots_project_display_and_inverse_hide_bits(bool effect, bool full, bool awakening, byte expected)
    {
        var blob = new byte[150];
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2), effect ? (ushort)256 : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(110), full ? ushort.MaxValue : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(148), awakening ? (ushort)1 : (ushort)0);
        var view = new ClientAppearanceOptions(blob, 3);
        Assert.Equal(effect, view.ShowGrowEffect); Assert.Equal(full, view.ShowFullAvatar);
        Assert.Equal(awakening, view.ShowAwakeningAvatar); Assert.Equal(expected, view.VisibilityBits);
        Array.Fill(blob, (byte)127);
        Assert.Equal(expected, view.VisibilityBits); // A snapshot never borrows a mutable upload buffer.
    }

    [Fact]
    public void Missing_blob_preserves_legacy_behavior_and_unknown_bits_survive_real_blob_projection()
    {
        for (var legacy = 0; legacy <= byte.MaxValue; legacy++)
            Assert.Equal((byte)legacy, new ClientAppearanceOptions(null, (byte)legacy).VisibilityBits);
        Assert.Equal(0xFD, new ClientAppearanceOptions(new byte[150], 0xE5).VisibilityBits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(149)]
    [InlineData(151)]
    public void Malformed_stored_blob_is_rejected_instead_of_silently_reading_partial_options(int length) =>
        Assert.Throws<ArgumentException>(() => new ClientAppearanceOptions(new byte[length], 3));
}
