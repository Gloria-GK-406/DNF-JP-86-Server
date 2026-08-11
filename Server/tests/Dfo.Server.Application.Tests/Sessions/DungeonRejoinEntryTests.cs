using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class DungeonRejoinEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_character_with_no_recorded_run_is_offered_nothing()
    {
        var result = await Service(new DungeonRejoinRegistry())
            .LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.NoOffer, result.DungeonRejoin.Outcome);
        Assert.False(result.DungeonRejoin.IsAvailable);
    }

    [Fact]
    public async Task A_detached_run_within_its_window_may_be_returned_to()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment());

        var result = await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.Available, result.DungeonRejoin.Outcome);
        Assert.True(result.DungeonRejoin.IsAvailable);
        Assert.Equal(42, result.DungeonRejoin.PartyId);
        Assert.Equal<ushort[]>([7, 8], [.. result.DungeonRejoin.ParticipantUserIds]);
    }

    /// <remarks>
    /// Each of these is a record that exists but cannot be used. Reporting them as "no
    /// offer" would make a registry going wrong look exactly like an ordinary character who
    /// was never in a dungeon.
    /// </remarks>
    [Fact]
    public async Task A_run_belonging_to_another_account_is_an_identity_mismatch()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment() with { AccountId = new AccountId(999) });

        var result = await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.IdentityMismatch, result.DungeonRejoin.Outcome);
    }

    [Fact]
    public async Task A_run_the_character_is_still_attached_to_is_not_something_to_return_to()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment() with { IsDetached = false });

        var result = await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.NotDetached, result.DungeonRejoin.Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ushort.MaxValue + 1)]
    public async Task A_run_whose_party_cannot_be_addressed_is_reported_as_such(int partyId)
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment() with { PartyId = partyId });

        var result = await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.PartyUnavailable, result.DungeonRejoin.Outcome);
    }

    [Fact]
    public async Task A_run_whose_window_has_passed_is_reported_as_expired()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment() with { ExpiresAt = Now.AddSeconds(-1) });

        var result = await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal(DungeonRejoinOutcome.Expired, result.DungeonRejoin.Outcome);
    }

    /// <remarks>
    /// The legacy lookup renews an idle deadline and retires lapsed records as a side effect
    /// of being asked. Those are lifecycle transitions; a question must not perform them, or
    /// merely composing an entry would silently keep a stale run alive.
    /// </remarks>
    [Fact]
    public async Task Consulting_the_registry_twice_leaves_it_unchanged()
    {
        var registry = new DungeonRejoinRegistry();
        var attachment = Attachment();
        registry.Attach(attachment);
        var service = Service(registry);

        var first = await service.LoadAsync(Subject(), CancellationToken.None);
        var afterFirst = registry.Find(new CharacterId(99));
        var second = await service.LoadAsync(Subject(), CancellationToken.None);
        var afterSecond = registry.Find(new CharacterId(99));

        Assert.Equal(first.DungeonRejoin, second.DungeonRejoin);
        Assert.Equal(attachment, afterFirst);
        Assert.Equal(attachment, afterSecond);
    }

    [Fact]
    public async Task An_expired_run_is_still_recorded_after_being_reported_expired()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment() with { ExpiresAt = Now.AddSeconds(-1) });

        await Service(registry).LoadAsync(Subject(), CancellationToken.None);

        Assert.NotNull(registry.Find(new CharacterId(99)));
    }

    [Fact]
    public void Releasing_removes_a_recorded_run()
    {
        var registry = new DungeonRejoinRegistry();
        registry.Attach(Attachment());

        Assert.True(registry.Release(new CharacterId(99)));
        Assert.False(registry.Release(new CharacterId(99)));
        Assert.Null(registry.Find(new CharacterId(99)));
    }

    private static SessionAdjacentEntryService Service(IDungeonRejoinRegistry rejoin) =>
        new(new SessionPresenceRegistry(),
            new PvpLobbyRegistry(),
            rejoin,
            new FixedTime(Now));

    private static SessionAdjacentSubject Subject() => new(
        new CharacterId(99),
        new AccountId(1),
        StubChannelEntryPolicy.GeneralChannel,
        TownId: 1,
        AreaId: 0);

    /// <summary>A clock that does not move, so expiry is decided by the data under test.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DungeonAttachment Attachment() => new(
        new CharacterId(99),
        new AccountId(1),
        PartyId: 42,
        ParticipantUserIds: [7, 8],
        IsDetached: true,
        ExpiresAt: Now.AddMinutes(5));
}
