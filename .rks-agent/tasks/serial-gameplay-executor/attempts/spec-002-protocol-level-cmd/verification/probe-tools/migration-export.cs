using System.Text.Json;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Networking;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Registration data only: never build a provider or resolve/start a service.
var services = new ServiceCollection();
services.AddLogging();
var configuration = new ConfigurationBuilder().Build();
new ApplicationModule().ConfigureServices(services, configuration);
new InfrastructureModule().ConfigureServices(services, configuration);
new PersistenceModule().ConfigureServices(services, configuration);
new NetworkingModule().ConfigureServices(services, configuration);
new ProtocolModule().ConfigureServices(services, configuration);
var messages = services.Select(item => item.ImplementationInstance).OfType<MessageHandlerRegistration>()
    .OrderBy(item => Convert.ToInt64(item.MessageType)).Select(item => new
    {
        id = "message:" + Convert.ToInt64(item.MessageType),
        message = item.MessageType.ToString(), value = Convert.ToInt64(item.MessageType),
        handler = item.HandlerType.FullName, handler_assembly = item.HandlerType.Assembly.GetName().Name,
        telemetry = item.IsTelemetry,
        message_clr_shape = MessageShape(item.HandlerType),
    }).ToArray();
var commands = services.Select(item => item.ImplementationInstance).OfType<CommandHandlerRegistration>()
    .OrderBy(item => item.CommandType.FullName, StringComparer.Ordinal).Select(item => new
    {
        id = "command:" + item.CommandType.FullName,
        command = item.CommandType.FullName, handler = item.HandlerType.FullName,
        invoker = item.InvokerType.FullName,
        result = item.CommandType.GetInterfaces().Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommand<>)).GetGenericArguments()[0].FullName,
        execution_kind = item.ExecutionKind.ToString(),
    }).ToArray();
File.WriteAllText(args.Single(), JsonSerializer.Serialize(new { messages, commands }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
static string? MessageShape(Type handler)
{
    for (var type = handler; type is not null; type = type.BaseType)
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ClientMessageHandler<>))
            return type.GetGenericArguments()[0].FullName;
    return null; // A non-generic/multi-shape handler must be inspected at its own source.
}
