using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.CompoundItem;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class CompoundItemErrorCodeTests
{
    [Theory]
    [InlineData(OpeningRejection.NoRoom, 0x04)]
    [InlineData(OpeningRejection.EquipmentUnpurified, 0x13)]
    [InlineData(OpeningRejection.EquipmentUnrepaired, 0xD9)]
    [InlineData(OpeningRejection.MissingMaterial, 0x16)]
    [InlineData(OpeningRejection.InvalidChoice, 0x02)]
    public void Refusals_use_the_A21_compound_failure_table(OpeningRejection reason, byte code) =>
        Assert.Equal(code, GameCompoundItemCommandHandler.ErrorCode(reason));
}
