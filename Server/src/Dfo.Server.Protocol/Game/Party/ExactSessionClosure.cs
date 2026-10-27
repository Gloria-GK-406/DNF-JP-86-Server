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

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>Physical recipients captured before this command starts asynchronous tower work.</summary>
internal sealed class ExactSessionClosure
{
    private readonly Dictionary<SelectedSessionIdentity, NetworkConnectionContext?> targets = [];

    internal void Capture(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
        IEnumerable<SelectedSessionIdentity> recipients)
    {
        foreach (var session in recipients.Distinct())
        {
            if (targets.ContainsKey(session)) continue;
            NetworkConnectionContext? physical = null;
            if (sessions.IsInWorld(session)
                && users.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && user.AccountId == session.AccountId && user.CurrentCharacterId == session.CharacterId)
                physical = user.Connection;
            // A failed initial capture is final too: later projections must not bind a replacement.
            targets.Add(session, physical);
        }
    }

    internal ValueTask RequestAsync(ISelectedSessionDomainService sessions, ICommandConnectionClosures closures, SelectedSessionIdentity session) =>
        sessions.IsInWorld(session) && targets.TryGetValue(session, out var physical) && physical is not null
            ? closures.RequestCloseAsync(physical, CancellationToken.None) : ValueTask.CompletedTask;

}
internal static class TownWeaknessProjection
{
    internal static ValueTask RegisterAsync(ISelectedSessionDomainService sessions, IWeaknessDomainService weakness,
        IGameCommandOutbox outbox, SelectedSessionIdentity session, CancellationToken token) => sessions.IsInWorld(session)
        ? outbox.RegisterNotiAsync(session.ConnectionId, new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value), token)
        : ValueTask.CompletedTask;
}
