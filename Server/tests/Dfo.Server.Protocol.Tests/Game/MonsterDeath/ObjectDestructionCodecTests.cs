using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;

namespace Dfo.Server.Protocol.Tests.Game.MonsterDeath;

[Trait("Category", "Unit")]
public sealed class ObjectDestructionCodecTests
{
    [Theory]
    [InlineData("01230000FFFF0000000000000000000000000000000000000000000000010074760040000000000000000000000000000000000000000000000000000000DD47376B")]
    [InlineData("01230000FFFF000000000000000000000000000000000000000000000001003A5D0040000000000000000000000000000000000000000000000000000000FBBC535C")]
    public void Two_real_objects_decode_as_template_code_not_monster_sequence(string hex)
    {
        var request = Assert.IsType<MonsterDeathRequest>(new MonsterDeathDecoder(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Protocol.Game.MonsterDeath.MonsterDeathDecoder>.Instance, diagnostics: new Dfo.Server.Abstractions.World.ExperienceDiagnostics(false)).Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.Equal(8961, request.ObjectCode);
        Assert.Equal(0, request.ActorSequence);
        Assert.Equal(ushort.MaxValue, request.WireUserId);
    }

    [Fact]
    public void Real_monster_with_an_attack_entry_reads_killer_after_the_u32_identity()
    {
        var body = Convert.FromHexString("B9AB0000020000000000000000009E0501000600000001110102002305010006009703CF00000000000C24004000000045040100000000008403EE00E00208010A003A00340000007B36777D");
        var request = Assert.IsType<MonsterDeathRequest>(new MonsterDeathDecoder(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Protocol.Game.MonsterDeath.MonsterDeathDecoder>.Instance, diagnostics: new Dfo.Server.Abstractions.World.ExperienceDiagnostics(false)).Decode(GameMessageMetadata.Empty, body));
        Assert.Null(request.ObjectCode);
        Assert.Equal(0xABB9, request.ActorSequence);
        Assert.Equal(2, request.WireUserId);
        body[22] = 255;
        Assert.Throws<InvalidDataException>(() => new MonsterDeathDecoder(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Protocol.Game.MonsterDeath.MonsterDeathDecoder>.Instance, diagnostics: new Dfo.Server.Abstractions.World.ExperienceDiagnostics(false)).Decode(GameMessageMetadata.Empty, body));
    }
}
