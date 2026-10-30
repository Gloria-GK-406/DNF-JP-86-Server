using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>A member leaves the party before leaving the run, and a refused leave changes nothing.</summary>
[Trait("Category", "Unit")]
public sealed class DungeonExitDomainServiceTests
{
    private static readonly CharacterId Caller = new(10);
    private static readonly CharacterId Target = new(20);

    [Fact]
    public async Task Leaving_the_party_comes_before_leaving_the_run()
    {
        var f = new Fixture();
        await f.Service.LeavePartyAsync(Caller, default);
        Assert.Equal(["party", "end:10"], f.Calls);
    }

    [Fact]
    public async Task Removing_a_member_ends_that_members_part_not_the_callers()
    {
        var f = new Fixture();
        await f.Service.RemoveFromPartyAsync(Caller, Target, default);
        Assert.Equal(["party", "end:20"], f.Calls);
    }

    [Fact]
    public async Task A_refused_party_change_does_not_touch_the_run()
    {
        var f = new Fixture { PartySucceeds = false };
        var result = await f.Service.LeavePartyAsync(Caller, default);
        Assert.False(result.Succeeded);
        Assert.Equal(["party"], f.Calls);
    }

    [Fact]
    public async Task Without_a_run_a_party_change_ends_nothing()
    {
        var f = new Fixture { InRun = false };
        await f.Service.LeavePartyAsync(Caller, default);
        Assert.Equal(["party"], f.Calls);
    }

    [Fact]
    public async Task Giving_up_outside_a_real_party_ends_the_run_alone()
    {
        var f = new Fixture { PartySucceeds = false };
        var result = await f.Service.GiveUpAsync(Caller, default, default);
        Assert.True(result.Ended);
        Assert.False(result.Party.Succeeded);
        Assert.Equal(["party", "end:10"], f.Calls);
    }

    [Fact]
    public async Task Giving_up_is_not_ended_twice_once_the_party_took_the_character_out()
    {
        var f = new Fixture { LeavesWithParty = true };
        var result = await f.Service.GiveUpAsync(Caller, default, default);
        Assert.True(result.Ended);
        Assert.Equal(["party", "end:10"], f.Calls);
    }

    private sealed class Fixture
    {
        private static readonly DungeonRunState Run = (DungeonRunState)RuntimeHelpers.GetUninitializedObject(typeof(DungeonRunState));
        private bool ended;

        public bool PartySucceeds { get; init; } = true;
        public bool InRun { get; init; } = true;
        public bool LeavesWithParty { get; init; }
        public List<string> Calls { get; } = [];

        public DungeonExitDomainService Service => new(
            DungeonClearSettlementDomainServiceTests.Stub<IPartyService>.Of((_, _) =>
            {
                Calls.Add("party");
                return PartySucceeds ? PartyOperationResult.Success() : PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);
            }),
            DungeonClearSettlementDomainServiceTests.Stub<IDungeonRunDepartureDomainService>.Of((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IDungeonRunDepartureDomainService.Find):
                        return InRun && !(LeavesWithParty && ended) ? Run : null;
                    case nameof(IDungeonRunDepartureDomainService.EndAsync):
                        Calls.Add($"end:{((CharacterId)args[0]!).Value}");
                        ended = true;
                        return ValueTask.FromResult(true);
                    default:
                        throw new NotSupportedException(method.Name);
                }
            }));
    }
}
