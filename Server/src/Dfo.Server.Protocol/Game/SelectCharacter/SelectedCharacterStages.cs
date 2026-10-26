using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed record PreparedSelectedCharacterSession(
    SelectedSessionIdentity Session,
    ActiveCharacterState Selected,
    ChannelEntryIdentity Channel,
    IReadOnlyList<SelectedSessionCleanupNotificationPlan> CleanupNotifications,
    IReadOnlyList<NetworkConnectionId> ConnectionsToClose);


public sealed record PublishedSelectedCharacterSession(
    TownParticipantPublicationPlan? TownPresence,
    PvpLobbySynchronizationState? PvpLobby);


public sealed record SelectedCharacterPvpPublication(PvpParticipantPublicationPlan Publication);


public sealed record SynchronizedSelectedCharacterQuests(
    SelectCharacterQuestSynchronization? Change);


public sealed record RevivedSelectedCharacterPet(SelectCharacterPetRevival? Change);


internal sealed class SelectCharacterPhaseFailure : Exception
{
    private SelectCharacterPhaseFailure(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose,
        IReadOnlyList<SelectedSessionCleanupNotificationPlan>? cleanupNotifications = null)
        : base("A SelectCharacter phase failed after the session was prepared.", failure)
    {
        Failure = ExceptionDispatchInfo.Capture(failure);
        ConnectionsToClose = connectionsToClose;
        CleanupNotifications = cleanupNotifications ?? [];
    }

    public ExceptionDispatchInfo Failure { get; }
    public IReadOnlyList<NetworkConnectionId> ConnectionsToClose { get; }
    public IReadOnlyList<SelectedSessionCleanupNotificationPlan> CleanupNotifications { get; }

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        params NetworkConnectionId[] connectionsToClose) =>
        new(failure, connectionsToClose);

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose) =>
        new(failure, connectionsToClose);

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose,
        IReadOnlyList<SelectedSessionCleanupNotificationPlan> cleanupNotifications) =>
        new(failure, connectionsToClose, cleanupNotifications);
}
