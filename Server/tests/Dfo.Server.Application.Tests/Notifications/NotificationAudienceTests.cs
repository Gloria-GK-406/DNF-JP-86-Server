using System.Reflection;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// An audience says who, in characters; something else answers where they are.
/// </summary>
/// <remarks>
/// The split is the whole point of the type. Before it, a domain service that wanted three
/// characters told something had to look their connections up itself, and so a service about
/// lobbies knew what a socket was. The cases here name characters and then ask what came back,
/// which is the pair a resolver that leaked connections back into the audience would fail.
/// </remarks>
public sealed class NotificationAudienceTests
{
    private static readonly CharacterId Standing = new(4001);
    private static readonly CharacterId Watching = new(4002);
    private static readonly CharacterId Gone = new(4003);

    /// <summary>An audience names characters; the resolver says which connections they are on.</summary>
    [Fact]
    public void An_audience_of_characters_resolves_to_the_connections_they_are_on()
    {
        var sessions = new SelectedSessionDomainService();
        var standing = Play(sessions, Standing, "conn-standing");
        var watching = Play(sessions, Watching, "conn-watching");

        var recipients = Resolver(sessions)
            .Resolve(new NotificationAudience.TheseCharacters([Standing, Watching]));

        Assert.Equal(
            [
                new NotificationRecipient(Standing, standing.ConnectionId),
                new NotificationRecipient(Watching, watching.ConnectionId),
            ],
            recipients);
    }

    /// <summary>A character nobody is playing is simply not there to be told.</summary>
    /// <remarks>
    /// Not an error and not a silent failure either: they are absent from the recipients, so
    /// nothing downstream counts them as a delivery that went wrong. Whatever they would have been
    /// told is already true, and they will see it when they next look.
    /// </remarks>
    [Fact]
    public void A_character_nobody_is_playing_is_not_a_recipient()
    {
        var sessions = new SelectedSessionDomainService();
        var watching = Play(sessions, Watching, "conn-watching");

        var recipients = Resolver(sessions)
            .Resolve(new NotificationAudience.TheseCharacters([Gone, Watching]));

        Assert.Equal(
            [new NotificationRecipient(Watching, watching.ConnectionId)],
            recipients);
    }

    /// <summary>Naming nobody who is here reaches nobody, rather than reaching everybody.</summary>
    [Fact]
    public void An_audience_whose_characters_are_all_away_resolves_to_nobody() =>
        Assert.Empty(Resolver(new SelectedSessionDomainService())
            .Resolve(new NotificationAudience.TheseCharacters([Gone])));

    /// <summary>The order the caller named them in is the order they come back in.</summary>
    /// <remarks>
    /// Delivery walks this list, so the order here is the order frames go out in. A resolver that
    /// sorted or grouped would silently reorder every migrated fan-out.
    /// </remarks>
    [Fact]
    public void Recipients_come_back_in_the_order_the_audience_named_them()
    {
        var sessions = new SelectedSessionDomainService();
        Play(sessions, Standing, "conn-standing");
        Play(sessions, Watching, "conn-watching");

        var recipients = Resolver(sessions)
            .Resolve(new NotificationAudience.TheseCharacters([Watching, Standing]));

        Assert.Equal([Watching, Standing], recipients.Select(value => value.CharacterId));
    }

    [Fact]
    public void A_scoped_audience_resolves_only_matching_current_presence_sessions()
    {
        var sessions = new SelectedSessionDomainService();
        var presence = new SessionPresenceDomainService();
        var standing = Play(sessions, Standing, "conn-standing");
        var watching = Play(sessions, Watching, "conn-watching", listenerPort: 10012);
        presence.Admit(new SessionPresence(Standing, 10011, 1, 2, true, standing));
        presence.Admit(new SessionPresence(Watching, 10012, 1, 2, true, watching));

        var recipients = new NotificationAudienceResolver(sessions, presence).Resolve(
            new NotificationAudience.Scoped(
                new TownAreaFilter(11, 1, 2)));

        Assert.Equal(
            [new NotificationRecipient(Standing, standing.ConnectionId, standing)],
            recipients);
    }

    [Fact]
    public void A_scoped_audience_rejects_a_presence_owned_by_a_replaced_session()
    {
        var sessions = new SelectedSessionDomainService();
        var presence = new SessionPresenceDomainService();
        var old = Play(sessions, Standing, "conn-old");
        presence.Admit(new SessionPresence(Standing, 10011, 1, 2, true, old));
        _ = sessions.Select(old with
        {
            ConnectionId = new NetworkConnectionId("conn-new"),
            ConnectionGeneration = 2,
        });

        var recipients = new NotificationAudienceResolver(sessions, presence).Resolve(
            new NotificationAudience.Scoped(new TownAreaFilter()));

        Assert.Empty(recipients);
    }

    [Fact]
    public void A_scoped_audience_requires_a_presence_directory()
    {
        var resolver = new NotificationAudienceResolver(new SelectedSessionDomainService());

        var failure = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(new NotificationAudience.Scoped(new TownAreaFilter())));

        Assert.Contains("require town areas", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exact_session_audience_rejects_a_replacement_session()
    {
        var sessions = new SelectedSessionDomainService();
        var old = Play(sessions, Standing, "conn-old", listenerPort: 10011);
        _ = sessions.Select(old with
        {
            ConnectionId = new NetworkConnectionId("conn-new"),
            ConnectionGeneration = 2,
            ListenerPort = 10012,
        });

        var recipients = Resolver(sessions).Resolve(
            new NotificationAudience.TheseSessions([old]));

        Assert.Empty(recipients);
    }

    [Fact]
    public void An_exact_session_audience_preserves_order_duplicates_and_identity()
    {
        var sessions = new SelectedSessionDomainService();
        var standing = Play(sessions, Standing, "conn-standing");
        var watching = Play(sessions, Watching, "conn-watching");

        var recipients = Resolver(sessions).Resolve(
            new NotificationAudience.TheseSessions([watching, standing, watching]));

        Assert.Equal(
            [
                new NotificationRecipient(Watching, watching.ConnectionId, watching),
                new NotificationRecipient(Standing, standing.ConnectionId, standing),
                new NotificationRecipient(Watching, watching.ConnectionId, watching),
            ],
            recipients);
    }

    [Fact]
    public void A_character_audience_preserves_duplicate_entries()
    {
        var sessions = new SelectedSessionDomainService();
        var standing = Play(sessions, Standing, "conn-standing");

        var recipients = Resolver(sessions).Resolve(
            new NotificationAudience.TheseCharacters([Standing, Standing]));

        Assert.Equal(
            [standing.ConnectionId, standing.ConnectionId],
            recipients.Select(value => value.ConnectionId));
    }

    /// <summary>
    /// An audience cannot be made from nothing outside the hierarchy, and the shapes offered are
    /// the ones written in it.
    /// </summary>
    /// <remarks>
    /// Deliberately not the stronger claim that no other shape can exist. A record that is not
    /// sealed must expose its copy constructor at least protected -- CS8878, the build fails if
    /// you try to hide it -- so an outside record holding an audience can derive through it and
    /// add whatever fields it likes. What this asserts is what is actually true: the constructor
    /// that makes one out of nothing is private, the other is the one the language insists on, and
    /// the shapes on offer are the nested siblings.
    ///
    /// What that leaves is covered by the case below, not by a stronger sentence here.
    /// </remarks>
    [Fact]
    public void The_only_way_to_make_an_audience_from_nothing_is_private_to_the_hierarchy()
    {
        var constructors = typeof(NotificationAudience).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.All(constructors, constructor => Assert.True(
            constructor.IsPrivate
            || constructor.GetParameters() is [{ ParameterType.Name: nameof(NotificationAudience) }],
            $"'{constructor}' would let a shape be declared outside the hierarchy."));
        Assert.Contains(constructors, constructor =>
            constructor.IsPrivate && constructor.GetParameters().Length == 0);
        Assert.Equal(
            [
                typeof(NotificationAudience.Scoped),
                typeof(NotificationAudience.TheseCharacters),
                typeof(NotificationAudience.TheseSessions),
            ],
            typeof(NotificationAudience)
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(typeof(NotificationAudience).IsAssignableFrom)
                .OrderBy(value => value.Name));
    }

    /// <summary>
    /// The combination this design is meant to rule out is constructible, and is named rather than
    /// answered with silence.
    /// </summary>
    /// <remarks>
    /// This is the shape the doc comment on <see cref="NotificationAudience"/> admits to: a record
    /// elsewhere derives through the copy constructor the language insists on, and carries both an
    /// audience and a channel. So "these three characters, and also a channel" is a value that can
    /// be made, and no wording upstairs should say otherwise.
    ///
    /// What stops it doing damage is here. It reaches the resolver's final arm and is named, which
    /// is the difference between finding out and watching a notification go to nobody on a quiet
    /// evening.
    /// </remarks>
    [Fact]
    public void The_shape_that_should_not_exist_is_named_rather_than_resolving_to_nobody()
    {
        var mixed = new CharactersAndAChannel(
            new NotificationAudience.TheseCharacters([Standing]), ListenerPort: 10011);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Resolver(new SelectedSessionDomainService()).Resolve(mixed));

        Assert.Contains(nameof(CharactersAndAChannel), error.Message, StringComparison.Ordinal);
    }

    private sealed record CharactersAndAChannel(NotificationAudience Source, int ListenerPort)
        : NotificationAudience(Source);

    private static SelectedSessionIdentity Play(
        SelectedSessionDomainService sessions,
        CharacterId characterId,
        string connection,
        int listenerPort = 10011)
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(characterId.Value),
            characterId,
            new NetworkConnectionId(connection),
            connectionGeneration: 1,
            listenerPort);
        _ = sessions.Select(session);
        return session;
    }

    private static NotificationAudienceResolver Resolver(SelectedSessionDomainService sessions) =>
        new(sessions, new SessionPresenceDomainService());
}
