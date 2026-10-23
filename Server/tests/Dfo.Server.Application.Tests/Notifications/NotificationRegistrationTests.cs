using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// A notification nobody handles is found while nobody is connected.
/// </summary>
/// <remarks>
/// The command registry deliberately declines this check, because a command with no handler throws
/// into the request that asked for it and somebody is there to see it. Nothing is waiting on a
/// notification: an unhandled one looks exactly like an evening when nobody was online. So it has
/// to be startup or never, and the cases here are what say it is startup.
/// </remarks>
public sealed class NotificationRegistrationTests
{
    /// <summary>A notification in a catalogued assembly with no handler stops the host.</summary>
    /// <remarks>
    /// The message names the type. Asserted by containment rather than equality because the
    /// catalogue is a whole assembly: every other case in this project declares its own probe
    /// notification, and an exact match would make each of them break this one.
    ///
    /// The second half is what makes the sweep's filter answerable. A notification that cannot be
    /// instantiated is not a gap, and <see cref="ShapeNoOneCanSend"/> is here so that dropping the
    /// filter shows up as a failure rather than as a startup that refuses to boot over an abstract
    /// declaration nobody can publish.
    /// </remarks>
    [Fact]
    public void A_catalogued_notification_with_no_handler_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NotificationHandlerRegistry(
                [],
                [new NotificationCatalogRegistration(typeof(NotificationRegistrationTests).Assembly)]));

        Assert.Contains($"'{nameof(UnhandledProbe)}'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ShapeNoOneCanSend), error.Message, StringComparison.Ordinal);
    }

    /// <summary>A catalogue that declares no notification is not a gap.</summary>
    /// <remarks>
    /// This says only that: an assembly with nothing to handle passes. It is not evidence about
    /// the filter -- the DTO assembly has no candidate types at all, so nothing is filtered
    /// -- and the case above is where the filter is actually answerable.
    /// </remarks>
    [Fact]
    public void An_assembly_that_declares_no_notification_leaves_nothing_to_handle() =>
        Assert.NotNull(new NotificationHandlerRegistry(
            [],
            [new NotificationCatalogRegistration(typeof(GameMessageMetadata).Assembly)]));

    /// <summary>The registry is asked for before the first hosted service starts.</summary>
    /// <remarks>
    /// This is the guarantee that matters, and it is stronger than registration order: the host
    /// materialises its hosted services together and starts them afterwards, so a constructor that
    /// throws lands before anything has started -- including a listener registered earlier. The
    /// probe below is registered first for exactly that reason, and its <c>Started</c> staying
    /// false is what says a port would not have been open.
    /// </remarks>
    [Fact]
    public async Task The_registry_is_asked_for_before_the_first_hosted_service_starts()
    {
        var probe = new ProbeListener();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHostedService>(probe);
        // The retry loop is a timer source on the executor; production composes both.
        builder.Services.AddGameplayExecution();
        builder.Services.AddNotificationDispatching();
        builder.Services.AddNotificationCatalog(typeof(NotificationRegistrationTests).Assembly);
        using var host = builder.Build();

        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.False(probe.Started, "A hosted service started before the notification check ran.");
        Assert.Contains($"'{nameof(UnhandledProbe)}'", Explain(error), StringComparison.Ordinal);
    }

    private static string Explain(Exception error) => error.InnerException is { } inner
        ? $"{error.Message} {Explain(inner)}"
        : error.Message;

    [Fact]
    public void A_notification_registered_twice_is_refused()
    {
        var registration = new NotificationHandlerRegistration(
            typeof(UnhandledProbe), typeof(ProbeHandler),
            typeof(NotificationComposer<UnhandledProbe>));

        var error = Assert.Throws<InvalidOperationException>(() =>
            new NotificationHandlerRegistry([registration, registration], []));

        Assert.Contains(nameof(UnhandledProbe), error.Message, StringComparison.Ordinal);
    }

    /// <summary>Asking about a notification nobody registered says so, naming it.</summary>
    [Fact]
    public void A_notification_nobody_registered_is_named_when_it_is_asked_about()
    {
        var registry = new NotificationHandlerRegistry([], []);

        var error = Assert.Throws<InvalidOperationException>(
            () => registry.GetRegistration(typeof(UnhandledProbe)));

        Assert.Contains(nameof(UnhandledProbe), error.Message, StringComparison.Ordinal);
    }

    /// <summary>Registration binds the notification, its handler and its composer together.</summary>
    [Fact]
    public void A_registered_handler_is_found_with_the_retry_limit_it_declared()
    {
        var services = new ServiceCollection()
            .AddNotificationHandler<UnhandledProbe, ProbeHandler>();

        var registration = new NotificationHandlerRegistry(
            services.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<NotificationHandlerRegistration>(),
            [])
            .GetRegistration(typeof(UnhandledProbe));

        Assert.Equal(typeof(ProbeHandler), registration.HandlerType);
        Assert.Equal(typeof(NotificationComposer<UnhandledProbe>), registration.ComposerType);
    }

    /// <summary>The modules are wired so the check comes before anything that opens a port.</summary>
    /// <remarks>
    /// Not the guarantee -- that is the case above, which holds whatever the order is. This one
    /// records the wiring: the application module is applied before the networking one, so the
    /// check is in place even for a host that started its services strictly in registration order.
    /// Comparing against everything networking registered, rather than against a named TouchSocket
    /// type, keeps the case about the ordering rather than about a library's internals.
    /// </remarks>
    [Fact]
    public void The_registry_is_asked_for_before_the_listeners_are_registered()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true",
            })
            .Build();
        var services = new ServiceCollection();
        new ApplicationModule().ConfigureServices(services, configuration);
        var beforeNetworking = services.Count;
        new NetworkingModule().ConfigureServices(services, configuration);

        var indexed = services.Select((descriptor, index) => (descriptor, index)).ToArray();
        var validation = indexed.Single(entry =>
            entry.descriptor.ServiceType == typeof(IHostedService)
            && entry.descriptor.ImplementationType?.Name
                == "NotificationHandlerRegistryValidationService").index;
        var networkingHosted = indexed
            .Where(entry => entry.index >= beforeNetworking
                && entry.descriptor.ServiceType == typeof(IHostedService))
            .Select(entry => entry.index)
            .ToArray();

        Assert.NotEmpty(networkingHosted);
        Assert.All(networkingHosted, index => Assert.True(
            validation < index,
            $"The notification check is registered at {validation}, after a listener at {index}."));
    }

    private sealed record UnhandledProbe(int Value) : INotification<UnhandledProbe>;

    /// <summary>A notification declaration nobody can publish, so nobody owes it a handler.</summary>
    private abstract record ShapeNoOneCanSend : INotification<ShapeNoOneCanSend>;

    private sealed class ProbeListener : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ProbeHandler : INotificationHandler<UnhandledProbe>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
            NotificationRecipient recipient,
            UnhandledProbe notification,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
    }
}
