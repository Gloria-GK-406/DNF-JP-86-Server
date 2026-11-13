using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AdventureGroup;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// NOTI 0x01A0: one empty frame per adventure-group level a character's level change raised, to
/// the account's session while it is in the world, told at most once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AdventureGroupPushTests
{
    private static readonly AccountId Account = new(41);
    private static readonly CharacterId Playing = new(410);
    private static readonly CharacterId Other = new(411);

    [Theory]
    [InlineData(2, 3, 1)]
    [InlineData(2, 4, 2)]
    public async Task A_rise_is_told_to_the_session_in_the_world_once_per_level(byte from, byte to, int frames)
    {
        var directory = TestSessions.Directory(out var state);
        var owner = directory.InWorld(state, Account, Playing, "adventure-1");
        var events = new ProjectedNotifications();
        var subscriber = new AdventureGroupLevelChangedSubscriber(Rising(new(Account, from, to)), directory, events);

        // A level change of another character of the account still reaches the playing session.
        await subscriber.HandleAsync(Envelope(new(Other, 1, 86)), default);

        var (audience, notification) = Assert.Single(events.Projected);
        Assert.Equal(new NotificationAudience.TheseSessions([owner]), audience,
            new SessionsAudienceComparer());
        var levelUp = Assert.IsType<AdventureGroupLevelUp>(notification);
        var composed = await new AdventureGroupLevelUpHandler(directory).ComposeAsync(
            new(owner.CharacterId, owner.ConnectionId, owner), levelUp, default);
        Assert.Equal(frames, composed.Count);
        Assert.All(composed, message => Assert.Equal(
            ServerMessageType.GameAdventureGroupLevelUp, Assert.IsType<AdventureGroupLevelUpNotice>(message).MessageType));
    }

    [Fact]
    public async Task No_rise_tells_nothing()
    {
        var directory = TestSessions.Directory(out var state);
        _ = directory.InWorld(state, Account, Playing, "adventure-2");
        var events = new ProjectedNotifications();

        await new AdventureGroupLevelChangedSubscriber(Rising(null), directory, events)
            .HandleAsync(Envelope(new(Playing, 86, 85)), default);

        Assert.Empty(events.Projected);
    }

    [Fact]
    public async Task An_account_not_in_the_world_is_told_nothing()
    {
        var directory = TestSessions.Directory(out var state);
        _ = directory.Selected(state, Account, Playing, "adventure-3");
        var events = new ProjectedNotifications();

        await new AdventureGroupLevelChangedSubscriber(Rising(new(Account, 2, 3)), directory, events)
            .HandleAsync(Envelope(new(Playing, 85, 86)), default);
        await new AdventureGroupLevelChangedSubscriber(Rising(new(new AccountId(99), 2, 3)), directory, events)
            .HandleAsync(Envelope(new(Playing, 85, 86)), default);

        Assert.Empty(events.Projected);
    }

    [Fact]
    public async Task A_recipient_that_is_not_the_owner_gets_no_frame()
    {
        var directory = TestSessions.Directory(out var state);
        var owner = directory.InWorld(state, Account, Playing, "adventure-4");

        var composed = await new AdventureGroupLevelUpHandler(directory).ComposeAsync(
            new(Other, owner.ConnectionId), new AdventureGroupLevelUp(owner, 2), default);

        Assert.Empty(composed);
    }

    /// <summary>
    /// Live-only and uncoalesced: the dispatcher commits the consumption before invoking, never
    /// replays it and skips a fact from an earlier process, so a level change is told at most once
    /// and two changes are never merged into one.
    /// </summary>
    [Fact]
    public void The_subscription_is_live_only_and_never_coalesced()
    {
        var services = new ServiceCollection();
        services.AddAdventureGroupSlice();

        var registration = Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DomainSubscriberRegistration>());
        Assert.Equal(typeof(CharacterLevelChanged), registration.PayloadType);
        Assert.Equal(AdventureGroupRegistration.LevelUpSubscriberId, registration.SubscriberId);
        Assert.Equal(DomainEventDeliveryPolicy.LiveOnly, registration.Policy);
        Assert.Null(registration.CoalescingKey(new CharacterLevelChanged(Playing, 1, 2)));
    }

    private static IAdventureGroupDomainService Rising(AdventureGroupLevelRise? rise) =>
        TestRequired.Absent<IAdventureGroupDomainService>((method, _) =>
            method.Name == nameof(IAdventureGroupDomainService.LevelRiseAsync) ? ValueTask.FromResult(rise) : null);

    private static DomainEventEnvelope<CharacterLevelChanged> Envelope(CharacterLevelChanged change) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), change);

    private sealed class ProjectedNotifications : IEventService
    {
        public List<(NotificationAudience Audience, object Notification)> Projected { get; } = [];

        public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, IDomainEvent => throw new NotSupportedException();

        public ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, IDomainEvent => throw new NotSupportedException();

        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience, TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification<TNotification>
        {
            Projected.Add((audience, notification));
            return ValueTask.FromResult<NotificationDelivery>(default!);
        }
    }

    private sealed class SessionsAudienceComparer : IEqualityComparer<NotificationAudience>
    {
        public bool Equals(NotificationAudience? x, NotificationAudience? y) =>
            x is NotificationAudience.TheseSessions left && y is NotificationAudience.TheseSessions right
            && left.Sessions.SequenceEqual(right.Sessions);

        public int GetHashCode(NotificationAudience obj) => 0;
    }
}
