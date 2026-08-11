using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Commands.Game;

/// <summary>
/// Enter the game with one of an account's characters.
/// </summary>
/// <param name="Channel">
/// The channel the connection arrived on. It is resolved before dispatch because entry
/// policy — admission, spawn, position persistence and duelling applicability — all follow
/// from it, and a command that had to ask which listener it came from would be reaching
/// back into the transport.
/// </param>
public sealed record SelectCharacterCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel) : ICommand<SelectCharacterResult>, IAccountCommand;

public sealed class SelectCharacterCommandHandler(ICharacterEntryService characterEntry)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context,
        SelectCharacterCommand command,
        CancellationToken cancellationToken) =>
        characterEntry.EnterAsync(
            command.AccountId,
            command.SlotIndex,
            command.Channel,
            cancellationToken);
}
