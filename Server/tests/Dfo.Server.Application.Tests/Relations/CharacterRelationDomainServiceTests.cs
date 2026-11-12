using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Abstractions.Relations.CharacterRelationAddOutcome;
using static Dfo.Server.Abstractions.Relations.CharacterRelationKind;
using static Dfo.Server.Abstractions.Relations.CharacterRelationRemoveOutcome;
using static Dfo.Server.Application.Tests.Relations.RelationFixture;

namespace Dfo.Server.Application.Tests.Relations;

/// <summary>The relation rules, run against the registered service and a real database.</summary>
/// <remarks>
/// Each refusal is checked to leave the table as it was, since an outcome that says "refused" over
/// a row that was written anyway would pass any test that only looked at the outcome.
/// </remarks>
public sealed class CharacterRelationDomainServiceTests
{
    [Fact]
    public async Task Adding_an_offline_friend_lists_it_for_the_owner_only_and_a_repeat_keeps_one_row()
    {
        await using var f = new RelationFixture();

        Assert.Equal(Added, await f.AddAsync(A, B, Friend));
        Assert.Equal([B], await f.TargetsAsync(A, Friend));
        Assert.Empty(await f.TargetsAsync(B, Friend));
        Assert.Equal([A], await f.Query.ObserversOfAsync(B, Friend, default));

        Assert.Equal(AlreadyPresent, await f.AddAsync(A, B, Friend));
        Assert.Equal(1, f.Rows());
    }

    [Fact]
    public async Task Adding_oneself_a_missing_or_a_deleted_character_is_refused_and_writes_nothing()
    {
        await using var f = new RelationFixture();
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));

        Assert.Equal(Self, await f.AddAsync(A, A, Friend));
        Assert.Equal(TargetMissing, await f.AddAsync(A, new CharacterId(9999), Friend));
        Assert.Equal(TargetMissing, await f.AddAsync(A, Deleted, Friend));

        Assert.Equal(1, f.Rows());
        Assert.Equal([B], await f.TargetsAsync(A, Friend));
    }

    [Theory]
    [InlineData(Friend)]
    [InlineData(Blacklist)]
    public async Task The_limit_admits_the_last_place_and_refuses_the_next_without_writing(CharacterRelationKind kind)
    {
        await using var f = new RelationFixture();
        var limit = f.Query.Limit(kind);
        Assert.Equal(64, limit);
        f.Relate(A, f.AddLiveCharacters(limit - 1), kind);

        Assert.Equal(Added, await f.AddAsync(A, B, kind));
        Assert.Equal(Full, await f.AddAsync(A, C, kind));
        Assert.Equal(limit, f.Rows());
        Assert.False(await f.Query.ExistsAsync(A, C, kind, default));

        // A full list still says a target already in it is there rather than that the list is full.
        Assert.Equal(AlreadyPresent, await f.AddAsync(A, B, kind));
        // The other kind has its own count.
        var other = kind == Friend ? Blacklist : Friend;
        Assert.Equal(Added, await f.AddAsync(A, C, other));
    }

    [Fact]
    public async Task A_deleted_target_does_not_take_up_a_place()
    {
        await using var f = new RelationFixture();
        f.Relate(A, f.AddLiveCharacters(63), Friend);
        f.Relate(A, [Deleted], Friend);

        Assert.Equal(63, (await f.Query.ListAsync(A, Friend, default)).Count);
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));
    }

    [Fact]
    public async Task Removing_a_friend_removes_only_that_direction_and_a_second_removal_is_missing()
    {
        await using var f = new RelationFixture();
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));
        Assert.Equal(Added, await f.AddAsync(B, A, Friend));

        Assert.Equal(Removed, await f.RemoveAsync(A, B, Friend));
        Assert.Empty(await f.TargetsAsync(A, Friend));
        Assert.Equal([A], await f.TargetsAsync(B, Friend));

        Assert.Equal(Missing, await f.RemoveAsync(A, B, Friend));
        Assert.Equal(1, f.Rows());
    }

    [Fact]
    public async Task Blacklist_follows_the_same_rules_with_its_own_outcomes()
    {
        await using var f = new RelationFixture();

        Assert.Equal(Added, await f.AddAsync(A, B, Blacklist));
        Assert.Equal(AlreadyPresent, await f.AddAsync(A, B, Blacklist));
        Assert.Equal(TargetMissing, await f.AddAsync(A, new CharacterId(9999), Blacklist));
        Assert.Equal(TargetMissing, await f.AddAsync(A, Deleted, Blacklist));
        Assert.Equal(Self, await f.AddAsync(A, A, Blacklist));
        Assert.True(await f.Query.ExistsAsync(A, B, Blacklist, default));
        Assert.False(await f.Query.ExistsAsync(B, A, Blacklist, default));
        Assert.Equal(1, f.Rows());

        Assert.Equal(Removed, await f.RemoveAsync(A, B, Blacklist));
        Assert.Equal(Missing, await f.RemoveAsync(A, B, Blacklist));
        Assert.Equal(0, f.Rows());
    }

    [Fact]
    public async Task Friend_and_blacklist_between_the_same_pair_do_not_touch_each_other()
    {
        await using var f = new RelationFixture();
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));
        Assert.Equal(Added, await f.AddAsync(A, B, Blacklist));
        Assert.Equal([B], await f.TargetsAsync(A, Friend));
        Assert.Equal([B], await f.TargetsAsync(A, Blacklist));

        Assert.Equal(Removed, await f.RemoveAsync(A, B, Blacklist));
        Assert.Equal([B], await f.TargetsAsync(A, Friend));
        Assert.False(await f.Query.ExistsAsync(A, B, Blacklist, default));

        Assert.Equal(Added, await f.AddAsync(A, B, Blacklist));
        Assert.Equal(Removed, await f.RemoveAsync(A, B, Friend));
        Assert.True(await f.Query.ExistsAsync(A, B, Blacklist, default));
        Assert.False(await f.Query.ExistsAsync(A, B, Friend, default));
    }

    [Fact]
    public async Task A_list_runs_oldest_first_then_by_target_and_carries_the_second_it_was_made()
    {
        await using var f = new RelationFixture();
        var first = f.Clock.Now.AddMilliseconds(900);
        f.Clock.Now = first;
        Assert.Equal(Added, await f.AddAsync(A, C, Friend));
        f.Clock.Now = first.AddMinutes(1);
        Assert.Equal(Added, await f.AddAsync(A, E, Friend));
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));

        var list = await f.Query.ListAsync(A, Friend, default);
        Assert.Equal([C, B, E], list.Select(x => x.Target));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(first.ToUnixTimeSeconds()), list[0].Since);
    }

    [Fact]
    public async Task Purging_a_character_clears_both_directions_of_both_kinds_and_names_its_friend_observers()
    {
        await using var f = new RelationFixture();
        Assert.Equal(Added, await f.AddAsync(A, C, Friend));
        Assert.Equal(Added, await f.AddAsync(B, C, Friend));
        Assert.Equal(Added, await f.AddAsync(E, C, Blacklist));
        Assert.Equal(Added, await f.AddAsync(A, C, Blacklist));
        Assert.Equal(Added, await f.AddAsync(C, A, Friend));
        Assert.Equal(Added, await f.AddAsync(C, B, Blacklist));
        Assert.Equal(Added, await f.AddAsync(A, B, Friend));
        Assert.Equal(Added, await f.AddAsync(E, A, Blacklist));

        // The deleting use case marks the character deleted and purges in one transaction; the
        // observers must still be read once the character itself is no longer live.
        var purge = await f.WriteAsync(async relations =>
        {
            await f.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterTable>().MarkDeletedAsync(C, default);
            return await relations.PurgeAsync(C, default);
        });

        Assert.Equal([A, B], purge.FriendObservers.OrderBy(x => x.Value));
        Assert.Equal(0, f.Rows($"owner_id={C.Value} OR target_id={C.Value}"));
        Assert.Equal(2, f.Rows());
        foreach (var owner in new[] { A, B, E })
            foreach (var kind in new[] { Friend, Blacklist })
                Assert.DoesNotContain(C, await f.TargetsAsync(owner, kind));
        Assert.Empty(await f.Query.ObserversOfAsync(C, Friend, default));
        Assert.Equal([B], await f.TargetsAsync(A, Friend));
    }

    [Fact]
    public async Task Every_write_refuses_without_a_writable_transaction()
    {
        await using var f = new RelationFixture();
        var relations = f.Relations;

        await Assert.ThrowsAsync<InvalidOperationException>(() => relations.AddAsync(A, B, Friend, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => relations.RemoveAsync(A, B, Friend, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => relations.PurgeAsync(A, default).AsTask());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var readOnly = f.Scope.Begin(readOnly: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => relations.AddAsync(A, B, Friend, default).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => relations.RemoveAsync(A, B, Friend, default).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => relations.PurgeAsync(A, default).AsTask());
        });
        Assert.Equal(0, f.Rows());
    }

    [Fact]
    public async Task A_rolled_back_use_case_leaves_no_relation()
    {
        await using var f = new RelationFixture();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var transaction = f.Scope.Begin();
            Assert.Equal(Added, await f.Relations.AddAsync(A, B, Friend, default));
        });
        Assert.Equal(0, f.Rows());
    }

    [Fact]
    public async Task An_owner_that_is_not_a_live_character_is_a_caller_error()
    {
        await using var f = new RelationFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.AddAsync(Deleted, B, Friend));
        Assert.Equal(0, f.Rows());
    }

    [Fact]
    public async Task The_query_and_the_writer_are_one_registered_instance()
    {
        await using var f = new RelationFixture();
        Assert.Same(f.Relations, f.Query);
    }
}

