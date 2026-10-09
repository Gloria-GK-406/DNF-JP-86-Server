using System.Reflection;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Protocol;

namespace Dfo.Server.Application.Tests.DomainEvents;

public sealed class EventNotificationBoundaryTests
{
    [Fact]
    public void Only_the_event_projection_adapter_and_notification_mechanism_depend_on_the_transport_bus()
    {
        var types = new[] { typeof(ApplicationModule).Assembly, typeof(ProtocolModule).Assembly }
            .SelectMany(assembly => assembly.GetTypes()).ToArray();
        Assert.Contains(types, type => type == typeof(NotificationProjectionSubscriber<>));
        Assert.Empty(types.Where(DependsOnBus).Where(type => !IsPermitted(type)).Select(type => type.FullName));
    }

    [Fact]
    public void The_rule_detects_a_business_component_that_injects_or_keeps_the_old_bus()
    {
        Assert.True(DependsOnBus(typeof(InvalidPublisher)));
        Assert.False(IsPermitted(typeof(InvalidPublisher)));
        Assert.True(IsPermitted(typeof(NotificationProjectionSubscriber<>)));
    }

    [Fact]
    public void The_source_sweep_covers_HTTP_and_all_production_projects()
    {
        var server = new DirectoryInfo(AppContext.BaseDirectory);
        while (server is not null && !File.Exists(Path.Combine(server.FullName, "86JP.Server.slnx"))) server = server.Parent;
        Assert.NotNull(server);
        var source = Path.Combine(server.FullName, "src");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Dfo.Server.Abstractions/Notifications/INotificationBus.cs",
            "Dfo.Server.Application/Notifications/NotificationBus.cs",
            "Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs",
            "Dfo.Server.Application/DomainEvents/EventService.cs"
        };
        var matches = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(source, path).Replace('\\', '/'), Text: File.ReadAllText(path)))
            .Where(file => file.Text.Contains("INotificationBus", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(matches);
        Assert.Empty(matches.Where(file => !allowed.Contains(file.Path)).Select(file => file.Path));
    }

    private static bool DependsOnBus(Type type) =>
        type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters()).Any(parameter => parameter.ParameterType == typeof(INotificationBus))
        || type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(INotificationBus));

    private static bool IsPermitted(Type type) => type == typeof(NotificationProjectionSubscriber<>)
        || type.Namespace == "Dfo.Server.Application.Notifications";

    private sealed class InvalidPublisher(INotificationBus notifications)
    {
        public INotificationBus Bus => notifications;
    }
}
