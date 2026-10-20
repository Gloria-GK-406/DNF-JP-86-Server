using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

internal sealed class CycleQuestResolutionViewHandler(ISelectedSessionDomainService sessions,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accountInventories,
    ICharacterDomainService characters,
    IQuestQueryDomainService quests,
    CycleQuestProjectionFence fence, TimeProvider clock) : INotificationHandler<CycleQuestResolutionView>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        CycleQuestResolutionView notification, CancellationToken ct)
    {
        var result = notification.Result;
        var session = sessions.FindByCharacter(result.Acceptance.CharacterId);
        if (session is null || session.AccountId != result.Acceptance.AccountId
            || session.ConnectionId.Value != result.Acceptance.ConnectionKey
            || session.ConnectionGeneration != result.Acceptance.ConnectionGeneration
            || recipient.CharacterId != session.CharacterId || recipient.ConnectionId != session.ConnectionId) return [];
        await fence.WaitAsync(result.RunId, ct).ConfigureAwait(false);
        if (sessions.FindByCharacter(session.CharacterId) != session) return [];
        // Do not remove a freshly reaccepted copy when delivery was delayed by another run.
        var accepted = await quests.ReadAcceptedAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        if (accepted.Any(x => x.Quest.QuestId == result.QuestId)) return [];
        var current = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (current is null) return [];
        var available = await quests.ReadAvailableAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var slots = new InventorySlotMappingRuleService();
        var frames = new List<IServerNetworkMessage>();
        if (result.Finished)
        {
            frames.Add(new FinishQuestAcknowledgement(true, result.QuestId,
                FinishQuestCommandHandler.ObjectiveKindOf(result.Objective), result.Experience,
                [.. result.Rewards.Select(x => new FinishedQuestItem(x.ItemId == 0 ? (ushort)0 : checked((ushort)slots.ToGameSlot(x.Slot).Index),
                    checked((uint)x.ItemId), checked((uint)x.Count)))], checked((uint)result.At.ToUnixTimeSeconds()), 0));
            // Current snapshots cannot replay stale consumed counts or lower a later experience total.
            var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            var account = await accountInventories.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            foreach (var bag in inventory.Containers.Where(x => x.ContainerType is InventoryContainerType.Main or InventoryContainerType.SharedMaterial))
                frames.Add(new InventorySnapshotMessage(bag, inventory.VirtualResources,
                    account.VirtualResources, account.Cargo.SelectionKey, account.Cargo.Value32, clock.GetUtcNow()));
            if (await characters.Get(session.CharacterId).RemainingSkillPointsAsync(ct).ConfigureAwait(false) is { } points)
                frames.Add(new ExperienceNotification(current.Level, current.Experience, points, AccountHonorLevel.Initial, ExperienceBonusLines.None));
        }
        else frames.Add(new GiveUpQuestAcknowledgement(true, result.QuestId, 0));
        frames.Add(new AcceptedQuestProgressMessage([.. accepted.Select(x => new AcceptedQuestProgressState(
            checked((ushort)x.Quest.QuestId), unchecked((uint)x.Quest.TriggerValue)))]));
        frames.Add(new AvailableQuestListMessage(current.Level, available));
        return frames;
    }
}
