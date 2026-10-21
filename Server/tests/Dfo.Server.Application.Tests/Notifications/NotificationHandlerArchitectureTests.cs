using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// What a notification handler is allowed to be given.
/// </summary>
/// <remarks>
/// A handler works out what one recipient should be told and writes nothing. Whatever changed has
/// already been committed by the command that caused the notification, so a handler that wrote
/// would be writing outside any transaction, taking the lock a player's own action is waiting on,
/// and doing it once per recipient.
/// <para>
/// The rule is read off the type, like the one for command handlers, and for the same reason: a
/// hand-kept list of approved names is a rule nobody maintains. What is allowed is narrower there
/// than here in one respect -- a command handler may take a domain store, and this may not,
/// because a store is where a write is settled.
/// </para>
/// </remarks>
public sealed class NotificationHandlerArchitectureTests
{
    /// <summary>
    /// Types that are not domain services and are still allowed, each with the reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> Exempt = new Dictionary<Type, string>
    {
    };

    private static readonly Assembly Abstractions = typeof(ICharacterDomainService).Assembly;

    /// <summary>
    /// The sweep actually finds handlers.
    /// </summary>
    /// <remarks>
    /// Without this, a reflection query that stopped matching would turn every check below into an
    /// assertion about an empty sequence, and an empty sequence has no violations in it. This
    /// project has already been bitten by a check that passed because it was looking at nothing.
    /// </remarks>
    [Fact]
    public void TheSweepFindsTheNotificationHandlers() =>
        Assert.NotEmpty(ProductionNotificationHandlers());

    [Fact]
    public void ProductionNotificationHandlers_OnlyTakeDomainServices()
    {
        var violations = ProductionNotificationHandlers()
            .SelectMany(handler => handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (Handler: handler, Parameter: parameter)))
            .Where(candidate => !IsPermitted(candidate.Parameter.ParameterType))
            .Select(candidate =>
                $"{candidate.Handler.Name}({candidate.Parameter.ParameterType.Name} "
                    + $"{candidate.Parameter.Name})")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// The rule refuses what it is meant to refuse.
    /// </summary>
    /// <remarks>
    /// Without this, a rule that accepted everything would pass the check above just as quietly as
    /// an assembly with no violations. The first four below are the ways a handler could reach a
    /// write: a store that settles one, a table underneath it, a command that performs one, and
    /// the bus that would dispatch that command. The fifth is not a way to reach anything -- it is
    /// there so that a rule which had quietly become "anything at all" fails here too.
    /// </remarks>
    [Theory]
    [InlineData(typeof(Dfo.Server.Abstractions.Inventory.IItemDomainStore))]
    [InlineData(typeof(Dfo.Server.Abstractions.Persistence.ICharacterTable))]
    [InlineData(typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionSnapshotQuery))]
    [InlineData(typeof(Dfo.Server.Application.Commands.ICommandBus))]
    [InlineData(typeof(string))]
    public void ATypeOutsideTheVocabularyIsRefused(Type type) => Assert.False(IsPermitted(type));

    [Theory]
    [InlineData(typeof(ICharacterDomainService))]
    [InlineData(typeof(Dfo.Server.Abstractions.World.ITownAreaDomainService))]
    public void ADomainServiceIsPermitted(Type type) => Assert.True(IsPermitted(type));

    /// <summary>Naming yourself a seam is not being one.</summary>
    /// <remarks>
    /// The rule has two halves and this is the one the cases above cannot fail on: without the
    /// assembly test, anything at all could be admitted by ending its name in DomainService, and
    /// a handler would reach a write by declaring an interface next to itself. The stand-in below
    /// lives in this test project, which is exactly where such a type would not belong.
    /// </remarks>
    [Fact]
    public void A_type_outside_the_seam_layer_is_refused_however_it_is_named() =>
        Assert.False(IsPermitted(typeof(ILocallyDeclaredDomainService)));

    /// <summary>Named like a seam, declared nowhere near one.</summary>
    private interface ILocallyDeclaredDomainService;

    /// <summary>Every exemption states why it is one.</summary>
    /// <remarks>
    /// Empty today, and the case stays: the moment somebody adds one, this is what asks them to
    /// say why.
    /// </remarks>
    [Fact]
    public void EveryExemptionCarriesItsReason() =>
        Assert.All(Exempt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));

    /// <summary>The assembly that declares the notifications is the one the check covers.</summary>
    /// <remarks>
    /// The startup check is closure over catalogued assemblies, and an assembly nobody catalogued
    /// is an assembly whose missing handlers nobody notices. Forgetting this line would not fail
    /// anything -- it would just quietly turn the check off for the only assembly that has
    /// notifications in it.
    /// </remarks>
    [Fact]
    public void The_assembly_that_declares_the_notifications_is_catalogued()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(
            services, new ConfigurationBuilder().Build());

        Assert.Contains(
            services.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<NotificationCatalogRegistration>(),
            registration => registration.Assembly == typeof(ProtocolModule).Assembly);
    }

    /// <summary>The migrated fan-out composes the frame it always composed.</summary>
    /// <remarks>
    /// One frame, carrying the newcomer the plan named. Before the migration the same message was
    /// built in the protocol handler from a target's participant; this is the same expression in
    /// its new home, and this case is what says so rather than an argument that it must be.
    /// </remarks>
    [Fact]
    public async Task The_pvp_publication_handler_composes_one_frame_for_the_newcomer()
    {
        var newcomer = new PvpParticipantEntryState(
            Character: null!, Public: null!, Equipment: [], Pets: []);

        var messages = await new PvpParticipantPublishedHandler().ComposeAsync(
            new NotificationRecipient(new CharacterId(11), new NetworkConnectionId("Game:11")),
            new PvpParticipantPublished(newcomer),
            CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Equal("PvpParticipantPublicationMessage", message.GetType().Name);
        Assert.Same(
            newcomer,
            message.GetType().GetProperty("Participant")!.GetValue(message));
    }

    /// <summary>Each notification declares its own allowance, and this is what it declared.</summary>
    /// <remarks>
    /// The number is a decision about what is being said -- whether arriving twice is worse than
    /// not arriving -- so it belongs to the notification and not to the machinery. Which also
    /// means nothing else would notice it changing: without this case, editing the literal is a
    /// one-character change that turns nothing red.
    /// </remarks>
    [Fact]
    public void The_pvp_publication_declares_the_allowance_it_meant_to()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        var registration = Assert.Single(
            services.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<NotificationHandlerRegistration>(),
            value => value.NotificationType == typeof(PvpParticipantPublished));
        Assert.Equal(1, registration.RetryLimit);
        Assert.Equal(typeof(PvpParticipantPublishedHandler), registration.HandlerType);
    }

    private static IEnumerable<Type> ProductionNotificationHandlers() =>
        typeof(ProtocolModule).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

    private static bool IsPermitted(Type type) => Exempt.ContainsKey(type) || IsDomainService(type);

    /// <summary>
    /// A domain service is an interface the seam layer publishes whose name says so.
    /// </summary>
    /// <remarks>
    /// Both halves matter, as in the command-handler rule: the suffix alone would let a type
    /// inside <c>Application</c> or <c>Protocol</c> claim to be a seam by naming itself one, and
    /// the assembly alone would admit every table gateway the seam layer also declares.
    /// </remarks>
    private static bool IsDomainService(Type type) =>
        type.IsInterface
        && type.Assembly == Abstractions
        && type.Name.EndsWith("DomainService", StringComparison.Ordinal);
}
