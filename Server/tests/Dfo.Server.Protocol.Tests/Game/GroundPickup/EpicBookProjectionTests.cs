using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

public sealed class EpicBookProjectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Epic_notification_carries_ids_and_cumulative_signed_safe_balances(byte mode)
    {
        var message = new EpicBookStateMessage(mode, [new(10300000, int.MaxValue), new(10300001, 27)]);
        var body = message.EncodeBody();
        Assert.Equal(0x0465, message.Type);
        Assert.Equal(21, body.Length);
        Assert.Equal(mode, body[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(10300000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(int.MaxValue, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(10300001, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(13)));
        Assert.Equal(27, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(17)));
    }
    [Fact]
    public void Selected_character_stream_contains_the_account_book_in_the_existing_position()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete() with { EpicPieces = [new EpicPieceBalance(10300000, 13)] };
        var messages = SelectedCharacterStateMessageFactory.Create(seed);
        var epic = Assert.Single(messages.OfType<EpicBookStateMessage>());
        Assert.Equal(0, epic.Mode);
        Assert.Equal(13, Assert.Single(epic.Pieces).Count);
        var index = messages.ToList().IndexOf(epic);
        Assert.IsType<GuildMedalBaselineMessage>(messages[index - 1]);
        Assert.IsType<UsableCountBaselineMessage>(messages[index + 1]);
    }
    [Fact]
    public void Empty_book_preserves_empty_baseline_and_invalid_projection_is_refused()
    {
        Assert.Equal(new byte[5], new EpicBookStateMessage(0, []).EncodeBody());
        Assert.Throws<InvalidDataException>(() => new EpicBookStateMessage(2, []).EncodeBody());
        Assert.Throws<InvalidDataException>(() => new EpicBookStateMessage(1, [new(1, -1)]).EncodeBody());
        Assert.Throws<InvalidDataException>(() => new EpicBookStateMessage(1, [new(1, 1), new(1, 2)]).EncodeBody());
    }
}
