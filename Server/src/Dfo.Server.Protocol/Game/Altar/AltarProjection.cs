using Dfo.Server.Abstractions.DomainEvents;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.Altar;

internal sealed record AltarStateChanged(AltarActionResult Action, DateTimeOffset At) : INotification<AltarStateChanged>;

internal sealed class AltarStateChangedHandler(IAltarDungeonDomainService altars,
    IDungeonRunQueryDomainService runs, ISelectedSessionDomainService sessions, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<AltarStateChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, AltarStateChanged notification, CancellationToken ct)
    {
        var action = notification.Action; var state = action.State;
        var run = runs.Find(recipient.CharacterId);
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && session != expected
            || run?.RunId != state.RunId || run.RoomVersion != state.RoomVersion
            || altars.Find(state.RunId)?.Generation != state.Generation) return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage>();
        if (action.DifficultyResolved) messages.Add(new AltarDifficultyResolvedMessage(state.Difficulty));
        if (action.DifficultyPrompt)
            messages.Add(new AltarDifficultyPromptMessage(state.CompletedRounds,
                checked((ushort)Math.Clamp(Math.Ceiling(((state.NextDeadline ?? notification.At) - notification.At).TotalSeconds), 1, ushort.MaxValue))));
        if (action.RoundStarted)
        {
            var remaining = (state.NextDeadline ?? notification.At) - notification.At;
            messages.Add(new AltarIntervalMessage(state.CompletedRounds + 1, (uint)Math.Clamp(remaining.TotalMilliseconds, 0, uint.MaxValue)));
        }
        if (action.Spawns.Count > 0) messages.Add(new AltarWaveMessage(action.Spawns, action.SpawnTail));
        if (action.ShowRanking)
        {
            var reward = state.Rewards.SingleOrDefault(x => x.Participant.CharacterId == recipient.CharacterId);
            messages.Add(new AltarRankingMessage((uint)Math.Clamp(((state.FinishedAt ?? notification.At) - state.StartedAt).TotalMilliseconds, 0, uint.MaxValue),
                checked((uint)state.CompletedRounds), checked((uint)state.Definition.MaxRounds), reward?.Experience ?? 0));
        }
        if (action.ShowRewards) messages.Add(new AltarRewardMessage(state));
        foreach (var award in action.Awards.Where(x => x.Participant.CharacterId == recipient.CharacterId))
        {
            if (award.Experience is { } experience && award.SkillPoints is { } points)
                messages.Add(new ExperienceNotification(experience.Level, experience.NotificationTotal, points, AccountHonorLevel.Initial, ExperienceBonusLines.Of(experience)));
            messages.Add(GameInventoryDelivery.CarriedGoldNotification(award.GoldBalance, notification.At));
            foreach (var group in (award.Delivery?.Inventory ?? []).GroupBy(x => slots.Project(x.Slot).Space))
                messages.Add(new InventoryDeltaMessage(group.Key, group.GroupBy(x => x.Slot).Select(x =>
                {
                    var last = x.Last(); return new Dfo.Server.Data.Inventory.InventoryItemState(null, slots.Project(last.Slot).Index, last.Snapshot.ToCore());
                }).ToArray(), notification.At));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
