using System.Buffers.Binary;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MercenaryInfo;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// Where the account's adventure group (冒险团) goes on the wire, with a non-zero level and points
/// so a field that stayed zero cannot pass.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AdventureGroupWireTests
{
    private static readonly AdventureGroupStanding Standing = new(3, 21570);

    /// <summary>
    /// USERINFO subtype 1 ends with a 9-byte constant, the level u8, the points i32 and a 16-byte
    /// constant.
    /// </summary>
    [Fact]
    public void Userinfo_one_tail_carries_the_level_and_points()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public with
        {
            Extras = source.CharacterEntry.Public.Extras with { AdventureGroup = Standing },
        };

        var body = StateSynchronizationPayloadCodec.EncodeExtendedUserState(source.Character,
            publicState, source.CharacterEntry.Skills, [], source.World.PreparedAt);

        Assert.Equal(3, body[^21]);
        Assert.Equal(21570, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(body.Length - 20)));
        Assert.Equal(0x6F, body[^26]); // last non-zero byte of the constant run before the level
        Assert.Equal(1, body[^10]);    // the 0x01 inside the constant run after the points
    }

    /// <summary>USERINFO subtype 3 (viewing another player) carries the level as u16 at body+3.</summary>
    [Fact]
    public void Userinfo_three_carries_the_viewed_accounts_level_at_offset_three()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public with
        {
            Extras = source.CharacterEntry.Public.Extras with { AdventureGroup = Standing },
        };

        var body = StateSynchronizationPayloadCodec.EncodeOtherCharacterDetail(source.Character,
            publicState, source.CharacterEntry.Skills, [], source.World.PreparedAt);

        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3)));
    }

    /// <summary>CMD 0x01BA answers <c>01, level, points, 00</c>, the same bytes as the entry batch.</summary>
    [Fact]
    public void Mercenary_info_reply_and_entry_frame_carry_the_adventure_group()
    {
        byte[] expected = [0x01, 3, 0x42, 0x54, 0x00, 0x00, 0x00];

        Assert.Equal(expected, GameCmdEnvelope.Accept(MercenaryInfoResponseEncoder.Roster(Standing)));
        Assert.Equal(expected, new MercenarySupportStateMessage(Standing).EncodeBody());
    }

    /// <summary>The roster header the entry batch sends keeps +5 and +6 at zero.</summary>
    [Fact]
    public async Task Entry_roster_header_leaves_the_adventure_group_out()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var selected = source.CharacterEntry with
        {
            Public = source.CharacterEntry.Public with
            {
                Extras = source.CharacterEntry.Public.Extras with { AdventureGroup = Standing },
            },
        };

        var roster = await CharacterStateProjection.BuildRosterEntryAsync(
            new(7), new AccountCharacterRoster(new(7), 24, []), new CharacterId(1), selected,
            TestRequired.Absent<Dfo.Server.Abstractions.Characters.ICharacterEntryStateDomainService>(), default);

        Assert.Equal(0, roster.ManageLevel);
        Assert.Equal(0, roster.AdventurePoint);
    }
}
