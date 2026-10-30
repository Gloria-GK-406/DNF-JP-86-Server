using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

public sealed class CharacterAppearanceInitializationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(33)]
    public void Closed_options_survive_both_roster_packets_without_changing_real_career(byte packedCareer)
    {
        var display = new CharacterAppearanceRuleService().Resolve(
            new(CharacterCareer.FromPacked(0, packedCareer)), [], new(new byte[150], 3));
        var summary = new CharacterSummary(new(2), 0, "1123"u8.ToArray(), 0, packedCareer, 86, 0, []) { Display = display };
        var first = AccountCharacterRosterPayloadCodec.Encode(new(new AccountId(4), 32, [summary]));
        var member = new CharacterRosterMemberState(summary.CharacterId, summary.SlotIndex, summary.Name,
            summary.Job, summary.GrowType, summary.Level, 0, 0, 0, [], 0) { Display = display };
        var later = SupplementalSynchronizationCodec.EncodeRoster(new(32, 32, 0, 0, 0, 0, [member])).Body.ToArray();
        Assert.Equal(25, first[^12]); // Tail+24: same reader as subtype0 and NOTI0165.
        Assert.Equal(first, later);
        Assert.Equal(packedCareer, first[31]);
    }
}
