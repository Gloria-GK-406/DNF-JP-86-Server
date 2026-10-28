using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// Where a character is standing, asked of the service that owns it.
/// </summary>
/// <remarks>
/// The position used to be a field on the session's snapshot of the character: every step read the
/// whole snapshot out, copied it with one record replaced and wrote the whole thing back, and every
/// reader had to find a session before it could ask. The cases here report a movement and then ask,
/// which is the pair that a service ignoring its reports would fail.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CharacterPositionTests
{
    private static readonly CharacterPositionState Gate = new(
        TownId: 3, AreaId: 6, X: 902, Y: 388, Direction: 1);

    /// <summary>Nobody is standing anywhere until entry says so.</summary>
    /// <remarks>
    /// Nothing is prepared for a character who is not being played, so the answer is nothing rather
    /// than an invented origin. Town zero, area zero is a place a real character can be standing.
    /// </remarks>
    [Fact]
    public void A_character_nobody_is_playing_is_standing_nowhere() =>
        TestGameplay.Run(() =>
        {
            Assert.Null(new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService()).PositionOf(Who));
        });

    [Fact]
    public void A_character_is_standing_where_entry_placed_them()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());

            positions.Begin(Session(), Gate, 100);

            Assert.Equal(Gate, positions.PositionOf(Who));
        });
    }

    /// <summary>
    /// A step reported, and then the question: the answer is where the step put them.
    /// </summary>
    /// <remarks>
    /// <b>The case a service that dropped its reports would fail.</b> Every other case here would
    /// pass against one that only ever answered what entry placed, because they do not move anyone
    /// and then ask.
    /// </remarks>
    [Fact]
    public void A_step_reported_is_what_the_next_question_answers()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var session = Session();
            positions.Begin(session, Gate, 100);

            var moved = positions.StepTo(session, x: 410, y: 275, direction: 5);

            Assert.Equal(new CharacterPositionState(3, 6, 410, 275, 5), moved!.Position);
            Assert.Equal(moved.Position, positions.PositionOf(Who));
        });
    }

    /// <summary>
    /// A step keeps the town and the area it was taken in.
    /// </summary>
    /// <remarks>
    /// This is the rule the walking command used to hold itself: a report of movement inside an
    /// area carries no area, and taking one from it would let a frame sent before a crossing arrive
    /// after it and undo it.
    /// </remarks>
    [Fact]
    public void A_step_leaves_the_town_and_the_area_where_they_were()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var session = Session();
            positions.Begin(session, Gate, 100);

            var moved = positions.StepTo(session, x: 1, y: 2, direction: 0);

            Assert.Equal(Gate.TownId, moved!.Position.TownId);
            Assert.Equal(Gate.AreaId, moved.Position.AreaId);
        });
    }

    /// <summary>
    /// A crossing reported, and then the question: the answer is the area it crossed into.
    /// </summary>
    /// <remarks>
    /// The facing is kept, because the command that reports a crossing does not carry one. A
    /// crossing that reset it would turn every town door into a character spinning round.
    /// </remarks>
    [Fact]
    public void A_crossing_reported_is_what_the_next_question_answers()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var session = Session();
            positions.Begin(session, Gate, 100);

            var crossed = positions.CrossInto(session, new TownAreaKey(11, 5, 2), x: 100, y: 200, sharesTownPresence: true);

            Assert.Equal(new TownAreaKey(11, 5, 2), crossed!.CurrentArea!.Key);
            Assert.Equal(new CharacterPositionState(5, 2, 100, 200, Gate.Direction), positions.PositionOf(Who));
        });
    }

    /// <summary>Reports arrive one after another and each one starts from the last.</summary>
    /// <remarks>
    /// A crossing then a step: the step must land in the town the crossing moved to, which is what
    /// a service holding its own record does and what reading a snapshot taken before the crossing
    /// would not.
    /// </remarks>
    [Fact]
    public void A_step_after_a_crossing_is_taken_in_the_area_crossed_into()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var session = Session();
            positions.Begin(session, Gate, 100);

            positions.CrossInto(session, new TownAreaKey(11, 5, 2), x: 100, y: 200, sharesTownPresence: true);
            var moved = positions.StepTo(session, x: 150, y: 210, direction: 3);

            Assert.Equal(new CharacterPositionState(5, 2, 150, 210, 3), moved!.Position);
        });
    }

    /// <summary>A session that is no longer the one standing there moves nobody.</summary>
    /// <remarks>
    /// The refusal the runtime directory used to make when a whole snapshot was written back to a
    /// generation that had been displaced. Without it a report still in flight from the connection
    /// that was replaced would move the character who replaced it.
    /// </remarks>
    [Fact]
    public void A_displaced_session_cannot_move_the_one_that_replaced_it()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var displaced = Session(generation: 1);
            var replacement = Session(generation: 2);
            positions.Begin(replacement, Gate, 100);

            Assert.Null(positions.StepTo(displaced, x: 1, y: 2, direction: 0));
            Assert.Null(positions.CrossInto(displaced, new TownAreaKey(11, 1, 1), x: 1, y: 2, sharesTownPresence: true));
            Assert.Null(positions.LeaveTown(displaced, TownAreaDepartureReason.DungeonEntry));
            Assert.Equal(Gate, positions.PositionOf(Who));
        });
    }

    /// <summary>Away from town the character stands in no area but keeps where it left.</summary>
    /// <remarks>
    /// The dungeon returns the character to that place and a logout while away stores it, so the
    /// position outlives the area membership; a step while away moves nobody.
    /// </remarks>
    [Fact]
    public void Away_from_town_the_place_left_is_kept_until_the_return()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var session = Session();
            positions.Begin(session, Gate, 100);

            var left = positions.LeaveTown(session, TownAreaDepartureReason.DungeonEntry);

            Assert.Equal(TownAreaDepartureReason.DungeonEntry, left!.DepartureReason);
            Assert.Null(positions.StandingOf(session));
            Assert.Null(positions.StepTo(session, x: 1, y: 2, direction: 0));
            Assert.Equal(Gate, positions.PositionOf(Who));

            var back = positions.ReturnToTown(session, new TownAreaKey(11, Gate.TownId, Gate.AreaId), Gate, true);

            Assert.Equal(TownAreaMembershipChangeKind.Joined, back!.Kind);
            Assert.Equal(Gate, positions.StandingOf(session)!.Position);
        });
    }

    /// <summary>A displaced session's teardown does not forget its replacement.</summary>
    [Fact]
    public void A_displaced_session_forgets_nothing()
    {
        TestGameplay.Run(() =>
        {
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var displaced = Session(generation: 1);
            var replacement = Session(generation: 2);
            positions.Begin(replacement, Gate, 100);

            Assert.False(positions.EndExact(displaced));
            Assert.Equal(Gate, positions.PositionOf(Who));

            Assert.True(positions.EndExact(replacement));
            Assert.Null(positions.PositionOf(Who));
        });
    }

    private static readonly CharacterId Who = new(60);

    private static SelectedSessionIdentity Session(long generation = 1) =>
        SelectedSessionIdentity.Create(
            new AccountId(1),
            Who,
            new NetworkConnectionId($"connection-{generation}"),
            generation,
            listenerPort: 10011);
}
