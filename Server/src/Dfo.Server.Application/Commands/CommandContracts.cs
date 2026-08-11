namespace Dfo.Server.Application.Commands;

using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

public enum ApplicationCommandType
{
    EstablishChannelConnection = 1,
    GetChannelScriptVersion = 2,
    GetChannelScript = 3,
    GetChannelInfo = 4,
    AcknowledgeGameExit = 5,
    LoginGameUser = 6,
    GetAccountCharacterRoster = 7,
    SelectCharacter = 8,
}

public enum CommandExecutionKind
{
    Direct = 0,
    Transactional = 1,
    Workflow = 2,
}

public enum UserAccessMode
{
    Unscoped = 0,
    Shared = 1,
    Exclusive = 2,
}

public interface ICommand
{
    ApplicationCommandType CommandType { get; }
}

public interface ICommand<out TResult> : ICommand;

public interface IOnlineUserCommand : ICommand
{
    OnlineUserId OnlineUserId { get; }
}

public interface IAccountCommand : ICommand
{
    AccountId AccountId { get; }
}

public interface ICommandBus
{
    ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default);
}

public sealed record CommandExecutionContext(
    Guid ExecutionId,
    ApplicationCommandType CommandType,
    Type HandlerType,
    CommandExecutionKind ExecutionKind,
    UserAccessMode OnlineUserAccessMode = UserAccessMode.Unscoped,
    UserAccessMode AccountAccessMode = UserAccessMode.Unscoped);

public delegate ValueTask<object?> CommandExecutionDelegate();

public interface ICommandPipelineBehavior
{
    int Order { get; }

    ValueTask<object?> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate next,
        CancellationToken cancellationToken);
}

public interface ICommandHandler
{
    bool CanHandle(ICommand command);

    ValueTask<object?> HandleAsync(
        CommandExecutionContext context,
        ICommand command,
        CancellationToken cancellationToken);
}

public abstract class CommandHandler<TCommand, TResult> : ICommandHandler
    where TCommand : class, ICommand<TResult>
{
    public bool CanHandle(ICommand command) => command is TCommand;

    public async ValueTask<object?> HandleAsync(
        CommandExecutionContext context,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (command is not TCommand typedCommand)
        {
            throw new InvalidOperationException(
                $"Handler '{GetType().Name}' cannot handle command CLR type '{command.GetType().Name}'.");
        }

        return await HandleCommandAsync(context, typedCommand, cancellationToken).ConfigureAwait(false);
    }

    protected abstract ValueTask<TResult> HandleCommandAsync(
        CommandExecutionContext context,
        TCommand command,
        CancellationToken cancellationToken);
}
