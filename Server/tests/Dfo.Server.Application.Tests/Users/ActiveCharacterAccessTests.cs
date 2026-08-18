using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Users;

/// <summary>
/// The read mirror and the multi-character write scope.
/// </summary>
/// <remarks>
/// These describe the two guarantees the scope is for. The first is that a transition holding
/// several characters cannot deadlock another one holding the same characters, however the two
/// name them. The second is that a transition applies completely or not at all, because a partly
/// applied one can leave two characters disagreeing about where they are, and this game has no
/// reconnect with which to resynchronise them.
/// </remarks>
public sealed class ActiveCharacterAccessTests
{
    [Fact]
    public void Read_answers_with_a_mirror_that_later_writes_do_not_change()
    {
        var fixture = new Fixture();
        fixture.Publish(1, fatigueUsed: 5);

        var mirror = fixture.Access.Read(new CharacterId(1));

        fixture.Publish(1, fatigueUsed: 9);

        Assert.NotNull(mirror);
        Assert.Equal(5, mirror!.Fatigue.Used);
        Assert.Equal(9, fixture.Access.Read(new CharacterId(1))!.Fatigue.Used);
    }

    [Fact]
    public void Read_omits_characters_that_are_not_active_rather_than_answering_null()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(3);

        var mirrors = fixture.Access.Read([new CharacterId(1), new CharacterId(2), new CharacterId(3)]);

        Assert.Equal([1, 3], mirrors.Keys.Select(value => value.Value).Order());
        Assert.Null(fixture.Access.Read(new CharacterId(2)));
    }

    /// <remarks>
    /// The reason the service sorts rather than trusting the caller. Both transitions want the same
    /// two characters and ask in opposite orders; with per-caller ordering this is the textbook
    /// deadlock, so a timeout here would be a real failure rather than a slow test.
    /// </remarks>
    [Fact]
    public async Task Opposing_acquisition_orders_do_not_deadlock()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(2);

        var ascending = Task.Run(async () =>
        {
            for (var round = 0; round < 40; round++)
            {
                await using var scope = await fixture.Access.AcquireAsync(
                    [new CharacterId(1), new CharacterId(2)], CancellationToken.None);
                await Task.Yield();
            }
        });
        var descending = Task.Run(async () =>
        {
            for (var round = 0; round < 40; round++)
            {
                await using var scope = await fixture.Access.AcquireAsync(
                    [new CharacterId(2), new CharacterId(1)], CancellationToken.None);
                await Task.Yield();
            }
        });

        await Task.WhenAll(ascending, descending).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task A_repeated_character_is_held_once_rather_than_waiting_for_itself()
    {
        var fixture = new Fixture();
        fixture.Publish(1);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1), new CharacterId(1)], CancellationToken.None);

        Assert.Equal([new CharacterId(1)], scope.Held);
    }

    [Fact]
    public async Task Held_characters_are_reported_in_the_order_they_were_locked()
    {
        var fixture = new Fixture();
        fixture.Publish(7);
        fixture.Publish(2);
        fixture.Publish(5);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(7), new CharacterId(2), new CharacterId(5)], CancellationToken.None);

        Assert.Equal([2, 5, 7], scope.Held.Select(value => value.Value));
    }

    [Fact]
    public async Task An_overlapping_scope_waits_for_the_first_to_be_returned()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(2);
        fixture.Publish(3);

        var first = await fixture.Access.AcquireAsync(
            [new CharacterId(1), new CharacterId(2)], CancellationToken.None);
        var second = fixture.Access
            .AcquireAsync([new CharacterId(2), new CharacterId(3)], CancellationToken.None)
            .AsTask();

        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        await using var granted = await second.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal([2, 3], granted.Held.Select(value => value.Value));
    }

    [Fact]
    public async Task A_disjoint_scope_is_granted_while_another_is_held()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(2);

        await using var first = await fixture.Access.AcquireAsync(
            [new CharacterId(1)], CancellationToken.None);
        await using var second = await fixture.Access
            .AcquireAsync([new CharacterId(2)], CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal([new CharacterId(2)], second.Held);
    }

    [Fact]
    public async Task Completing_applies_every_staged_change()
    {
        var fixture = new Fixture();
        fixture.Publish(1, fatigueUsed: 0);
        fixture.Publish(2, fatigueUsed: 0);

        await using (var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1), new CharacterId(2)], CancellationToken.None))
        {
            foreach (var characterId in scope.Held)
            {
                scope[characterId] = scope[characterId] with
                {
                    Fatigue = new CharacterFatigueState(188, 4),
                };
            }

            scope.Complete();
        }

        Assert.Equal(4, fixture.Access.Read(new CharacterId(1))!.Fatigue.Used);
        Assert.Equal(4, fixture.Access.Read(new CharacterId(2))!.Fatigue.Used);
    }

    /// <remarks>
    /// The reason applying is not done at disposal: <c>await using</c> disposes on the exception
    /// path too, so a transition that threw half way through would otherwise commit its first half.
    /// </remarks>
    [Fact]
    public async Task Returning_a_scope_without_completing_it_discards_the_staged_changes()
    {
        var fixture = new Fixture();
        fixture.Publish(1, fatigueUsed: 0);
        fixture.Publish(2, fatigueUsed: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = await fixture.Access.AcquireAsync(
                [new CharacterId(1), new CharacterId(2)], CancellationToken.None);
            scope[new CharacterId(1)] = scope[new CharacterId(1)] with
            {
                Fatigue = new CharacterFatigueState(188, 4),
            };
            throw new InvalidOperationException("the transition failed after staging one change");
        });

        Assert.Equal(0, fixture.Access.Read(new CharacterId(1))!.Fatigue.Used);
        Assert.Equal(0, fixture.Access.Read(new CharacterId(2))!.Fatigue.Used);
    }

    /// <remarks>
    /// A second login for the same account displaces the active character, and that path does not
    /// take this scope's lease. Applying the departed session's state over the arriving one is the
    /// terminal kind of divergence, so the whole scope is refused rather than partly written.
    /// </remarks>
    [Fact]
    public async Task Completing_applies_nothing_when_one_character_was_displaced_while_held()
    {
        var fixture = new Fixture();
        fixture.Publish(1, fatigueUsed: 0);
        fixture.Publish(2, fatigueUsed: 0);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1), new CharacterId(2)], CancellationToken.None);
        foreach (var characterId in scope.Held)
        {
            scope[characterId] = scope[characterId] with
            {
                Fatigue = new CharacterFatigueState(188, 4),
            };
        }

        fixture.ReplaceActiveCharacter(accountOf: 2, withCharacter: 22);

        var error = Assert.Throws<InvalidOperationException>(scope.Complete);

        Assert.Contains("no part of this scope was applied", error.Message);
        Assert.Equal(0, fixture.Access.Read(new CharacterId(1))!.Fatigue.Used);
    }

    [Fact]
    public async Task A_character_that_is_not_active_is_refused_rather_than_silently_skipped()
    {
        var fixture = new Fixture();
        fixture.Publish(1);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Access.AcquireAsync(
                [new CharacterId(1), new CharacterId(2)], CancellationToken.None));
    }

    /// <remarks>
    /// The refusal above must also return what it took. Were the first lease leaked, this second
    /// acquisition would wait for a holder that no longer exists.
    /// </remarks>
    [Fact]
    public async Task A_refused_acquisition_returns_the_leases_it_had_already_taken()
    {
        var fixture = new Fixture();
        fixture.Publish(1);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Access.AcquireAsync(
                [new CharacterId(1), new CharacterId(2)], CancellationToken.None));

        await using var scope = await fixture.Access
            .AcquireAsync([new CharacterId(1)], CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal([new CharacterId(1)], scope.Held);
    }

    /// <remarks>
    /// Contention that outlives the budget is reported, not waited out. A hang leaves an
    /// investigator nothing to read, and this server would rather refuse the entry and let the
    /// client ask again than proceed on an uncertain hold.
    /// </remarks>
    [Fact]
    public async Task Acquisition_that_cannot_be_granted_in_time_is_reported_rather_than_waited_out()
    {
        var fixture = new Fixture(acquisitionTimeout: TimeSpan.FromMilliseconds(50));
        fixture.Publish(1);

        await using var holder = await fixture.Access.AcquireAsync(
            [new CharacterId(1)], CancellationToken.None);

        var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await fixture.Access.AcquireAsync([new CharacterId(1)], CancellationToken.None));

        Assert.Contains("was not granted within", error.Message);
    }

    [Fact]
    public async Task A_slot_cannot_be_given_the_state_of_a_different_character()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(2);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1), new CharacterId(2)], CancellationToken.None);

        Assert.Throws<InvalidDataException>(() =>
            scope[new CharacterId(1)] = scope[new CharacterId(2)]);
    }

    [Fact]
    public async Task A_character_the_scope_does_not_hold_is_not_readable_through_it()
    {
        var fixture = new Fixture();
        fixture.Publish(1);
        fixture.Publish(2);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1)], CancellationToken.None);

        Assert.Throws<KeyNotFoundException>(() => scope[new CharacterId(2)]);
    }

    [Fact]
    public async Task An_empty_set_is_rejected()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Access.AcquireAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task Completing_twice_is_refused()
    {
        var fixture = new Fixture();
        fixture.Publish(1);

        await using var scope = await fixture.Access.AcquireAsync(
            [new CharacterId(1)], CancellationToken.None);
        scope.Complete();

        Assert.Throws<InvalidOperationException>(scope.Complete);
    }

    /// <summary>One store, its four services, and characters published through the real path.</summary>
    private sealed class Fixture
    {
        private readonly OnlineStateStore store = new();
        private readonly OnlineUserDomainService online;
        private readonly ActiveCharacterDomainService active;

        public Fixture(TimeSpan? acquisitionTimeout = null)
        {
            online = new OnlineUserDomainService(store);
            active = new ActiveCharacterDomainService(store);
            Access = new ActiveCharacterAccessDomainService(
                store,
                new UserExecutionGate<CharacterId>(),
                acquisitionTimeout ?? TimeSpan.FromSeconds(20));
        }

        public IActiveCharacterAccessDomainService Access { get; }

        /// <summary>Brings one account online and gives it an active character.</summary>
        public void Publish(int characterId, ushort fatigueUsed = 0)
        {
            var accountId = new AccountId(characterId);
            Authenticate(accountId);
            active.Publish(accountId, Character(characterId, fatigueUsed));
        }

        /// <summary>Swaps which character an account has active, as a second login would.</summary>
        public void ReplaceActiveCharacter(int accountOf, int withCharacter) =>
            active.Publish(new AccountId(accountOf), Character(withCharacter, 0));

        private void Authenticate(AccountId accountId)
        {
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId($"Game:character-access-{accountId.Value}"),
                NetworkEndpointKind.Game,
                10011);
            var onlineUser = online.RegisterConnection(connection);
            store.Authenticate(onlineUser.OnlineUserId, accountId);
        }

        private static ActiveCharacterState Character(int characterId, ushort fatigueUsed) =>
            new(
                new CharacterIdentityState(new CharacterId(characterId), 0, [0x41], 1, 0),
                new CharacterProgressionState(1, 0),
                new CharacterFatigueState(188, fatigueUsed),
                new CharacterPositionState(1, 0, 474, 234, 5),
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
                null);
    }
}
