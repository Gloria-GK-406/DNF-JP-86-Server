using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record GamePremiumServiceUsageReportCommand(NetworkDispatchContext Dispatch, PremiumServiceUsageReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GamePremiumServiceUsageReportCommandHandler(ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService usage, IDungeonPremiumBuffDomainService buffs, TimeProvider clock,
    ICommandWorkUnits units, IGameCommandOutbox sender, ILogger<PremiumServiceUsageReportHandler> logger)
    : ICommandHandler<GamePremiumServiceUsageReportCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GamePremiumServiceUsageReportCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || message.Slot is not (3 or 4)) return false;
        var runId = message.Slot == 3 ? runs.Find(session.CharacterId)?.RunId : null;
        if (!sessions.IsInWorld(session)) return false;
        var state = message.Slot == 3
            ? await units.WriteAsync(execution, new("ConsumeDungeonBuff", []),
                (_, token) => ConsumeAsync(session, runId, token), cancellationToken).ConfigureAwait(false)
            : await units.ReadOnlyAsync(execution, new("ReadPremiumSnapshot", []),
                token => PremiumSnapshotReader.ReadAsync(sessions, premiums, usage, clock, session, token), cancellationToken).ConfigureAwait(false);
        if (!state.IsCurrent) return false;
        // Effects already ran locally. Only slot3 consumes daily usage; never apply effects again.
        await sender.RegisterCmdAsync(connection,
            new PremiumServiceStateMessage(state.Entitlements, state.Usage, state.PreparedAt), cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Premium service usage report character={CharacterId} slot={Slot}: snapshot refreshed",
            session.CharacterId, message.Slot);
        return true;
    }

    private async ValueTask<PremiumSnapshotResult> ConsumeAsync(SelectedSessionIdentity session,
        DungeonRunId? runId, CancellationToken token)
    {
        if (!sessions.IsInWorld(session))
            return new(new([]), [], clock.GetUtcNow(), false);
        if (runId is { } capturedRunId)
            await buffs.TryConsumeAsync(session.AccountId, session.CharacterId, capturedRunId, token).ConfigureAwait(false);
        return new(new(await premiums.For(session.AccountId).ReadAsync(token).ConfigureAwait(false)),
            await usage.ReadAsync(session.CharacterId, token).ConfigureAwait(false), clock.GetUtcNow());
    }
}
