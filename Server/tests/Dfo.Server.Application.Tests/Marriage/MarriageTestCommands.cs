using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Marriage;

// Domain characterization keeps its result shape and real transaction; network cases use the registered whole CMD.
internal sealed record ProposalTestCommand(SelectedSessionIdentity Actor, SelectedSessionIdentity Peer, byte? Response)
    : ICommand<MarriageProposalResult>, Dfo.Server.Testing.IDomainFixtureParticipants
{
    public SelectedSessionIdentity? Session => Actor;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => [Actor, Peer];
    public IReadOnlyList<SelectedSessionIdentity> Participants => [Actor,Peer];
}
internal sealed class ProposalTestHandler(IMarriageCoupleDomainService couple) : ICommandHandler<ProposalTestCommand, MarriageProposalResult>
{
    public ValueTask<MarriageProposalResult> HandleAsync(CommandExecutionContext context, ProposalTestCommand command, CancellationToken ct) =>
        command.Response is { } response ? couple.RespondProposalAsync(command.Actor, command.Peer, response, ct)
            : couple.ProposeAsync(command.Actor, command.Peer, ct);
}
internal sealed record CeremonyTestCommand(SelectedSessionIdentity Actor, SelectedSessionIdentity Peer, int Tier, int WaitIndex, byte? Response)
    : ICommand<MarriageCeremonyResult>, Dfo.Server.Testing.IDomainFixtureParticipants
{
    public SelectedSessionIdentity? Session => Actor;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => [Actor, Peer];
    public IReadOnlyList<SelectedSessionIdentity> Participants => [Actor,Peer];
}
internal sealed class CeremonyTestHandler(IMarriageCoupleDomainService couple) : ICommandHandler<CeremonyTestCommand, MarriageCeremonyResult>
{
    public async ValueTask<MarriageCeremonyResult> HandleAsync(CommandExecutionContext context, CeremonyTestCommand command, CancellationToken ct)
    {
        var result = command.Response is { } response
            ? await couple.RespondReservationAsync(command.Actor, command.Peer, response, ct)
            : await couple.RequestReservationAsync(command.Actor, command.Peer, command.Tier, command.WaitIndex, ct);
        if (result.Changed && result.Ceremony is { State: MarriageCeremonyState.Reserved } ceremony)
            result = result with { Admissions = await couple.OpenWeddingAsync(ceremony, ct) };
        return result;
    }
}
internal static class MarriageTestCommands
{
    internal static void Configure(IServiceCollection services)
    {
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        Dfo.Server.Testing.RecordedOutbound.AddTo(services);
        services.AddSingleton<IGameCommandOutbox, Outbox>();
        services.AddFixtureCommand<ProposalTestCommand, MarriageProposalResult, ProposalTestHandler>(FixtureUnitKind.Transactional);
        services.AddFixtureCommand<CeremonyTestCommand, MarriageCeremonyResult, CeremonyTestHandler>(FixtureUnitKind.Transactional);
    }
    private sealed class Outbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => sender.SendCmdAsync(id, message, ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => sender.SendNotiAsync(id, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) => sender.SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => sender.SendExplicitAsync(id, delivery, ct);
    }
    internal static NetworkDispatchContext Dispatch(IServiceProvider services, SelectedSessionIdentity actor)
    {
        var users = services.GetRequiredService<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>();
        if (!users.TryGetByConnection(actor.ConnectionId, out var user) || user is null) throw new InvalidOperationException();
        return new(user.Connection);
    }
}
