using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record MarriageCeremonyCommand(SelectedSessionIdentity Actor, SelectedSessionIdentity Peer,
    int Tier, int WaitIndex, byte? Response) : ICommand<MarriageCeremonyResult>, IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity? Session => Actor;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => [Actor, Peer];
}

public sealed class MarriageCeremonyCommandHandler(IMarriageCoupleDomainService couple)
    : ICommandHandler<MarriageCeremonyCommand, MarriageCeremonyResult>
{
    public async ValueTask<MarriageCeremonyResult> HandleAsync(CommandExecutionContext context, MarriageCeremonyCommand command, CancellationToken ct)
    {
        var result = command.Response is { } response
            ? await couple.RespondReservationAsync(command.Actor, command.Peer, response, ct).ConfigureAwait(false)
            : await couple.RequestReservationAsync(command.Actor, command.Peer, command.Tier, command.WaitIndex, ct).ConfigureAwait(false);
        if (result.Changed && result.Ceremony is { State: MarriageCeremonyState.Reserved } ceremony)
            result = result with { Admissions = await couple.OpenWeddingAsync(ceremony, ct).ConfigureAwait(false) };
        return result;
    }
}
