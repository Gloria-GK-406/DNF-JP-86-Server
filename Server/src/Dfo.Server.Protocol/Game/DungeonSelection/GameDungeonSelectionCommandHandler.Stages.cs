using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game.DungeonSelection;

using Dfo.Server.Protocol.Game.Party;

internal sealed partial class GameDungeonSelectionCommandHandler
{
    private readonly ExactSessionClosure closeTargets = new();

    private SelectedSessionRecipient CaptureParticipant(SelectedSessionIdentity session) =>
        GameParticipantCapture.Capture(onlineUsers, session);

    private ValueTask<int> TownEnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) =>
        TownArrivalProjection.EnterAsync(sessions, towns, housing, townAreas, onlineCharacters, currentViews, outbox,
            weaknessState, projectWeakness, projections,
            new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), entries, token);

    internal ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => TownEnterAsync(entries, token);

    private async ValueTask ReturnTownIsolatedAsync(IReadOnlyList<TownAreaEntry> entries,
        List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token)
    {
        if (entries.Count == 0) return;
        try { _ = await ReturnTownAsync(entries, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            foreach (var entry in entries) failures.Add((entry.Session, error));
        }
    }
}
