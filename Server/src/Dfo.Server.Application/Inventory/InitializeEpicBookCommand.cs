using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
namespace Dfo.Server.Application.Inventory;

public sealed record InitializeEpicBookCommand : ICommand<bool>;
internal sealed class InitializeEpicBookCommandHandler(IEpicBookDomainService book) : ICommandHandler<InitializeEpicBookCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, InitializeEpicBookCommand command, CancellationToken token)
    {
        await book.InitializeAsync(token).ConfigureAwait(false);
        return true;
    }
}
