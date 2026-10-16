using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record MarriageCeremonyCommand(SelectedSessionIdentity Actor, SelectedSessionIdentity Peer,
    int Tier, int WaitIndex, byte? Response) : ICommand<MarriageCeremonyResult>, IMultiAccountCommand, IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity? Session => Actor;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => [Actor, Peer];
    public IReadOnlyList<AccountId> AccountIds => [Actor.AccountId, Peer.AccountId];
}

public sealed class MarriageCeremonyCommandHandler(IMarriageCeremonyDomainService ceremonies)
    : ICommandHandler<MarriageCeremonyCommand, MarriageCeremonyResult>
{
    public ValueTask<MarriageCeremonyResult> HandleAsync(CommandExecutionContext context, MarriageCeremonyCommand command, CancellationToken ct) =>
        command.Response is { } response
            ? ceremonies.RespondAsync(command.Actor, command.Peer, response, ct)
            : ceremonies.RequestAsync(command.Actor, command.Peer, command.Tier, command.WaitIndex, ct);
}
