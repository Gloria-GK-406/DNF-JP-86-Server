using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.TownProjection;

internal sealed class TownReturnCommandStages(
    ITownAreaDomainService townAreas, ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews, ITownMovementDomainService towns,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing,
    ICommandProjections notifications, IPartyTownProjectionDelivery partyTownProjection,
    Func<NetworkConnectionId, GameExplicitMessageDelivery, CancellationToken, ValueTask> register,
    Func<SelectedSessionIdentity, CancellationToken, ValueTask>? weakness)
    : PartyMutationProtocolCommandHandler(selectedSessions, null, townAreas, notifications, null!, null,
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, currentViews: currentViews, towns: towns,
        housing: housing, register: register, weakness: weakness)
{
    internal ValueTask<int> EnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => TownEnterAsync(entries, token);
    internal ValueTask PublishCurrentRosterCoreAsync(TownAreaKey key, CancellationToken token) => TownPublishCurrentRosterCoreAsync(key, token);
    internal ValueTask PublishArrivalCoreAsync(SelectedSessionIdentity newcomer, TownPresenceSynchronizationState state,
        IReadOnlyList<SelectedSessionIdentity> peers, CancellationToken token) => TownPublishArrivalCoreAsync(newcomer, state, peers, token);
    internal ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>>
        VisibleParticipantsAsync(TownAreaSnapshot area, SelectedSessionIdentity viewer, CancellationToken token) => TownVisibleParticipantsAsync(area, viewer, token);
    protected override ValueTask PartyTownRefreshMovedPartyAsync(SelectedSessionIdentity mover, PartyDirectoryScope? previousScope,
        CancellationToken token) => partyTownProjection.RefreshMovedPartyAsync(mover, previousScope, token);
    protected override ValueTask PartyTownRefreshObservedPartiesAsync(IReadOnlyList<SelectedSessionIdentity> recipients,
        IReadOnlyList<SelectedSessionIdentity> represented, CancellationToken token) => partyTownProjection.RefreshObservedPartiesAsync(recipients, represented, token);
}
