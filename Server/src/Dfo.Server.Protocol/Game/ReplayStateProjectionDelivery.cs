using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

/// <summary>Replaces current client state after a committed operation; never repeats the original ACK or reward.</summary>
internal sealed class ReplayStateProjectionDelivery(ICommandBus commands, SelectedSessionSafety safety,
    SelectedSessionTransitionGate transitions, IGameMessageSender sender) : IReplayStateProjectionDelivery
{
    public async ValueTask RefreshAsync(NetworkConnectionContext originalConnection, SelectedSessionIdentity session,
        ReplayProjectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalConnection);
        request.Validate();
        var recipient = safety.Capture(session);
        if (session.ConnectionId != originalConnection.ConnectionId
            || recipient.Connection is null || !ReferenceEquals(recipient.Connection, originalConnection)
            || safety.IsBlocked(originalConnection))
            throw new InvalidOperationException("Replay projection requires its exact original connection.");
        await using var lease = await transitions.BorrowOrAcquireExclusiveAsync(session.AccountId, ct).ConfigureAwait(false);
        safety.DemandCurrent(recipient);
        var snapshot = await commands.SendAsync(new ReadReplayStateCommand(session, request), ct).ConfigureAwait(false);
        var messages = Create(snapshot);
        safety.DemandCurrent(recipient);
        await sender.SendBatchAsync(originalConnection, messages, ct).ConfigureAwait(false);
    }

    internal static ImmutableArray<IGameServerMessage> Create(ReplayStateSnapshot state)
    {
        state.Request.Validate();
        var needs = state.Request.Needs;
        var messages = ImmutableArray.CreateBuilder<IGameServerMessage>();
        if (needs.HasFlag(ReplayProjectionNeeds.ItemUsage))
            messages.AddRange(ItemUsageCountMessage.FullSnapshot(state.ItemUsage));
        var inventory = state.Inventory?.Character();
        var account = state.Inventory?.Account();
        if (needs.HasFlag(ReplayProjectionNeeds.Inventory))
        {
            if (inventory is null || account is null) throw Missing("Inventory");
            InventoryContainerType[] order = [InventoryContainerType.Main, InventoryContainerType.Avatar,
                InventoryContainerType.PersonalCargo, InventoryContainerType.Equipment, InventoryContainerType.Pet,
                InventoryContainerType.AccountCargo];
            foreach (var type in order)
            {
                var container = type == InventoryContainerType.AccountCargo ? account.Cargo.Container
                    : inventory.Containers.SingleOrDefault(x => x.ContainerType == type) ?? throw Missing($"Inventory.{type}");
                messages.Add(new InventorySnapshotMessage(container, inventory.VirtualResources, account.VirtualResources,
                    account.Cargo.SelectionKey, account.Cargo.Value32, state.At));
            }
            var mapping = new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService();
            foreach (var slot in state.Request.TouchedSlots.Distinct())
            {
                var physical = mapping.ToGameSlot(slot);
                var container = physical.Space == InventoryContainerType.AccountCargo ? account.Cargo.Container
                    : inventory.Containers.Single(x => x.ContainerType == physical.Space);
                if (container.Items.All(x => x.SlotIndex != physical.Index))
                    messages.Add(GameInventoryDelivery.ClearedSlotNotification(physical.Space, physical.Index, state.At));
            }
        }
        var pets = state.Pets.Select(x => x.ToState()).ToImmutableArray();
        if (needs.HasFlag(ReplayProjectionNeeds.Pets)) messages.Add(new CreatureListMessage(pets));
        var skills = state.Skills?.ToState();
        if (needs.HasFlag(ReplayProjectionNeeds.Skills))
        {
            if (skills is null) throw Missing("Skills");
            if (skills.Pages.Any(x => x.PageIndex is < 0 or > 1)
                || skills.Pages.GroupBy(x => x.PageIndex).Any(x => x.Count() > 1))
                throw new InvalidDataException("Replay skills must fit the A21 two-page projection.");
            messages.Add(new SkillStateMessage(skills));
        }
        if (needs.HasFlag(ReplayProjectionNeeds.Quests))
        {
            var quests = state.Quests ?? throw Missing("Quests");
            messages.Add(new AcceptedQuestProgressMessage(quests.Carried.Select(x =>
                new AcceptedQuestProgressState(checked((ushort)x.QuestId), unchecked((uint)x.TriggerValue))).ToImmutableArray()));
            messages.Add(new QuestVisibilityMessage(quests.Visibility.ToArray()));
            messages.Add(new AvailableQuestListMessage((state.Progress ?? throw Missing("Progress")).Level, new(quests.Offers)));
        }
        if (needs.HasFlag(ReplayProjectionNeeds.Experience))
        {
            var progress = state.Progress ?? throw Missing("Progress");
            messages.Add(new ExperienceNotification(progress.Level, progress.Experience,
                state.Points ?? throw Missing("Points"), AccountHonorLevel.Initial, ExperienceBonusLines.None));
        }
        if (needs.HasFlag(ReplayProjectionNeeds.Fatigue))
            messages.Add(new FatigueNotification(state.Fatigue ?? throw Missing("Fatigue")));
        if (needs.HasFlag(ReplayProjectionNeeds.Premium))
            messages.Add(new PremiumServiceStateMessage(new(state.Premiums), state.PremiumUsage, state.At));
        if (needs.HasFlag(ReplayProjectionNeeds.CharacterAppearance))
        {
            var appearance = state.Appearance ?? throw Missing("Appearance");
            if (inventory is null || skills is null) throw Missing("Appearance inventory/skills");
            var equipment = inventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items;
            messages.Add(new CurrentUserStateMessage(appearance.Character(), appearance.Public(), equipment, pets));
            messages.Add(new ExtendedUserStateMessage(appearance.Character(), appearance.Public(), skills, equipment, state.At));
        }
        return messages.ToImmutable();
    }
    private static InvalidDataException Missing(string name) => new($"Required replay projection '{name}' is unavailable.");
}
