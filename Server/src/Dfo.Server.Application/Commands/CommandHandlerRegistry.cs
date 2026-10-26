using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public enum CommandSelectedSessionCleanupCapability { None, SelectedSessionLifecycle }
public enum CommandSelfSettlementCapability { None, SelfSettledAction }
public enum InternalActionCancellationCapability { None, ConfirmedRollbackWriteResult }
public enum TimerReadOnlyCancellationCapability { None, PremiumSnapshot, ItemUsageSnapshot }
public enum CommandRouteKind { Protocol, Internal }
public sealed record ProtocolCommandMessageBinding(ClientMessageType MessageType, Type MessageClrType);

public sealed record CommandHandlerRegistration(
    Type CommandType,
    Type HandlerType,
    Type InvokerType,
    CommandRouteKind Route,
    IReadOnlyList<ProtocolCommandMessageBinding>? ProtocolMessages = null,
    IReadOnlyCollection<CommandRegistrationExit>? RegistrationExits = null,
    CommandSelfSettlementCapability SelfSettlement = CommandSelfSettlementCapability.None,
    CommandSelectedSessionCleanupCapability SelectedSessionCleanup = CommandSelectedSessionCleanupCapability.None,
    TimerReadOnlyCancellationCapability TimerReadOnlyCancellation = TimerReadOnlyCancellationCapability.None,
    InternalActionCancellationCapability InternalActionCancellation = InternalActionCancellationCapability.None)
{
    internal CommandHandlerRegistration Freeze() => ProtocolMessages is null && RegistrationExits is null ? this : this with
    { ProtocolMessages = ProtocolMessages is null ? null : Array.AsReadOnly(ProtocolMessages.ToArray()),
      RegistrationExits = RegistrationExits is null ? null : Array.AsReadOnly(RegistrationExits.ToArray()) };
}

/// <summary>
/// Maps a command type to how it is executed.
/// </summary>
/// <remarks>
/// Commands are keyed by their own CLR type. There is no completeness check: unlike a
/// client message, which arrives off the wire and must always find a handler, a command
/// only exists because code constructed it, so an unregistered one is a problem at
/// dispatch rather than at startup.
/// </remarks>
public sealed class CommandHandlerRegistry
{
    private readonly IReadOnlyDictionary<Type, CommandHandlerRegistration> registrations;

    public CommandHandlerRegistry(IEnumerable<CommandHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var registrationList = registrations.Select(item => item.Freeze()).ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.CommandType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Command '{duplicate.Key.Name}' was registered more than once.");
        }

        foreach (var item in registrationList)
        {
            if (!Enum.IsDefined(item.InternalActionCancellation) || item.InternalActionCancellation != InternalActionCancellationCapability.None && item.Route != CommandRouteKind.Internal)
                throw new InvalidOperationException("Action cancellation requires its named internal registration.");
            if (!Enum.IsDefined(item.TimerReadOnlyCancellation) || item.TimerReadOnlyCancellation != TimerReadOnlyCancellationCapability.None && item.Route != CommandRouteKind.Internal)
                throw new InvalidOperationException("Timer snapshot cancellation requires a named internal registration.");
            if (!Enum.IsDefined(item.SelectedSessionCleanup)
                || item.SelectedSessionCleanup != CommandSelectedSessionCleanupCapability.None
                    && (item.Route != CommandRouteKind.Protocol || item.ProtocolMessages is not { Count: 1 }
                        || item.ProtocolMessages.Any(x => x.MessageType is not (ClientMessageType.GameSelectCharacter or ClientMessageType.GameReturnToSelection)
                            || typeof(IEffectSettlementRequest).IsAssignableFrom(x.MessageClrType))))
                throw new InvalidOperationException("Selected-session cleanup requires its named non-effect lifecycle protocol binding.");
            if (!Enum.IsDefined(item.SelfSettlement)) throw new InvalidOperationException("Unknown self-settlement capability.");
            if (item.SelfSettlement != CommandSelfSettlementCapability.None
                && (item.Route != CommandRouteKind.Protocol || item.ProtocolMessages is not { Count: > 0 }
                    || item.ProtocolMessages.Any(x => typeof(IEffectSettlementRequest).IsAssignableFrom(x.MessageClrType))))
                throw new InvalidOperationException("Self settlement requires non-effect protocol bindings.");
            if (!Enum.IsDefined(item.Route)) throw new InvalidOperationException("Unknown command route.");
            var protocol = typeof(IProtocolCommand).IsAssignableFrom(item.CommandType);
            if (protocol != (item.Route == CommandRouteKind.Protocol))
                throw new InvalidOperationException($"Command '{item.CommandType.Name}' has an incompatible source route.");
            if (item.Route == CommandRouteKind.Protocol)
            {
                if (item.ProtocolMessages is not { Count: > 0 } bindings
                    || bindings.Select(x => (x.MessageType, x.MessageClrType)).Distinct().Count() != bindings.Count
                    || bindings.Any(x => !IsConcreteMessageType(x.MessageClrType)))
                    throw new InvalidOperationException("A protocol command requires unique typed message bindings.");
                if (item.RegistrationExits is { Count: > 0 } exits
                    && (exits.Any(exit => !Enum.IsDefined(exit)) || exits.Distinct().Count() != exits.Count
                        || (exits.Contains(CommandRegistrationExit.SettledRequiredExplicit) || exits.Contains(CommandRegistrationExit.SettledInitialBatch))
                            && bindings.Any(x => !typeof(IEffectSettlementRequest).IsAssignableFrom(x.MessageClrType))
                        || exits.Contains(CommandRegistrationExit.CommittedReplayCurrentStateBatch)
                            && !bindings.Any(x => typeof(IEffectSettlementRequest).IsAssignableFrom(x.MessageClrType))))
                    throw new InvalidOperationException("Registration exits require compatible effect bindings.");
            }
            else if (item.ProtocolMessages is { Count: > 0 } || item.RegistrationExits is { Count: > 0 })
                throw new InvalidOperationException("Only a protocol command may bind messages.");
        }
        if (registrationList.Where(item => item.Route == CommandRouteKind.Protocol)
            .SelectMany(item => item.ProtocolMessages!.Select(binding => (binding.MessageType, item.CommandType)))
            .GroupBy(item => item.MessageType).Any(group => group.Select(item => item.CommandType).Distinct().Count() != 1))
            throw new InvalidOperationException("A logical message can belong to only one protocol command.");
        this.registrations = registrationList.ToDictionary(item => item.CommandType);
    }

    internal static bool IsConcreteMessageType(Type? type) => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
        && typeof(IClientNetworkMessage).IsAssignableFrom(type);

    public CommandHandlerRegistration GetRegistration(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        return registrations.TryGetValue(commandType, out var registration)
            ? registration
            : throw new InvalidOperationException(
                $"Command '{commandType.Name}' has no registered handler.");
    }
}
