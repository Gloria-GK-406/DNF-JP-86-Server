using Dfo.Server.Protocol.Game.Party;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
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




using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;
namespace Dfo.Server.Protocol.Game.DungeonEntry;
internal sealed partial class GameSelectDungeonCommandHandler
{
    internal async ValueTask<bool> EnterBossAsync(
        CommandExecutionContext execution,
        NetworkConnectionId requesterConnection,
        SelectedSessionIdentity requester,
        PartySnapshot roster,
        DungeonEntryIntent intent,
        CancellationToken token, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0)
    {
        var attachments = await BossEntryResolveAttachmentsAsync(requester, roster, token).ConfigureAwait(false);
        if (attachments is null)
        {
            RestoreEntryIntent(requester, intent);
            await BossEntryRefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, token).ConfigureAwait(false);
            return true;
        }

        var capturedConnections = attachments.ToDictionary(value => value.Session, value => CaptureEntryConnection(value.Session));
        Exception? deliveryFailure = null;
        var committed = false;
        var isolate = new List<SelectedSessionIdentity>();
        try
        {
            if (!BossEntryCurrent(attachments) || !BossEntryPartyCurrent(requester, roster, attachments))
            {
                RestoreEntryIntent(requester, intent);
                await BossEntryRefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, token).ConfigureAwait(false);
                return true;
            }

            var entered = await AdmitBossAsync(execution, attachments.Select(value => new TowerEntryMember(value.Session, value.Slot)).ToArray(), requester, unchecked((uint)Random.Shared.Next()), token, requestMode, difficulty, practiceMode).ConfigureAwait(false);
            if (entered.Run is null)
            {
                RestoreEntryIntent(requester, intent);
                await BossEntryRefuseAsync(requesterConnection,
                    entered.Failure is { } failure ? DungeonEntryRefusalCodes.For(failure, false) : entered.Rejection switch
                    {
                        "LevelTooLow" => SelectDungeonRefusal.LevelTooLow,
                        "InsufficientMaterial" or "MissingRequiredItem" => SelectDungeonRefusal.MissingRequiredItem,
                        "InvalidParty" or "NoSession" or "AlreadyInDungeon" => SelectDungeonRefusal.InvalidSelectionState,
                        _ => SelectDungeonRefusal.DungeonUnavailable,
                    },
                    token, entered.BlockedSlot ?? 0).ConfigureAwait(false);
                return true;
            }

            committed = true;
            isolate.AddRange(attachments.Select(value => value.Session));
            var run = entered.Run;
            if (!BossEntryCurrent(attachments, run))
                throw new InvalidOperationException("Tower cohort changed after committed admission.");

            foreach (var member in attachments.Where(value => value.Session != requester))
            {
                if (sessions.IsInWorld(member.Session))
                    entryIntents.Consume(member.Session);
            }

            var refreshed = new Dictionary<CharacterId, SelectCharacterResult>();
            foreach (var member in attachments.OrderBy(value => value.Slot))
            {
                // The attachment was loaded before admission. Attribute refresh alone keeps
                // its inventory snapshot, so reread after the temporary bindings commit.
                var runtime = await currentViews.TryLoadAsync(member.Session, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Tower member view disappeared after admission.");
                refreshed[member.Session.CharacterId] = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
            }

            var ordered = attachments.OrderBy(value => value.Slot).ToArray();
            var wireUsers = ordered.Select(value => value.Session.WireUserId).ToImmutableArray();
            var hostSlot = ordered.Single(value => value.Session == requester).Slot;
            var batches = new List<(SelectedSessionIdentity Session, IReadOnlyList<IGameServerMessage> Frames)>();
            foreach (var receiver in ordered)
            {
                var frames = new List<IGameServerMessage>();
                // A21 recalculates player attributes on DELETE_ITEM. Deliver the committed
                // debit before 008E/008F switch it into tower mode, whose table can still be null.
                foreach (var debit in entered.InventoryChanges.Where(value => value.CharacterId == receiver.Session.CharacterId))
                    BossEntryAppendDebit(frames, debit, clock.GetUtcNow());
                foreach (var source in ordered
                    .OrderBy(value => value.Session == receiver.Session ? 0 : 1)
                    .ThenBy(value => value.Slot))
                {
                    var state = refreshed[source.Session.CharacterId];
                    var equipment = state.CharacterInventory.Containers
                        .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
                    frames.Add(new ExtendedUserStateMessage(
                        state.Character, state.CharacterEntry.Public, state.CharacterEntry.Skills,
                        equipment, state.World.PreparedAt,
                        source.Session == receiver.Session ? null : source.Session.WireUserId));
                }
                frames.AddRange(await HousingCurrentProjection.ReadAsync(housing, receiver.Session, token).ConfigureAwait(false));
                frames.Add(new EnterSelectDungeonAcknowledgement());
                frames.Add(new UserStateNotification(wireUsers, 0x01));
                frames.Add(new UdpHostNotification(hostSlot));
                frames.Add(new SupportTagNotification());
                frames.Add(new BossTowerInfoNotification(run.DungeonId, run.TotalLevels));
                frames.Add(new BossTowerMapNotification(run.Room));
                batches.Add((receiver.Session, frames));
            }

            // XO-3: every member's batch is delivered before the requester's, each BestEffort.
            using (ordering.Begin("XO-3 boss tower entry", OutboundDelivery.BestEffort))
            {
                foreach (var batch in batches.OrderBy(value => value.Session == requester ? 1 : 0))
                    await outbox.RegisterExplicitAsync(batch.Session.ConnectionId,
                        new GameExplicitMessageDelivery(batch.Frames, []), token).ConfigureAwait(false);
            }
            await TownDepartureProjection.TownLeaveManyAsync(sessions, townAreas, projections, projectionState, currentViews, parties, ordered.Select(value => value.Session).ToArray(),
                TownAreaDepartureReason.DungeonEntry, token).ConfigureAwait(false);
        }
        catch (Exception error) when (committed)
        {
            deliveryFailure = error;
        }

        if (deliveryFailure is not null)
        {
            logger.LogWarning(deliveryFailure,
                "Boss tower entry projection failed after commit; isolating {Count} exact sessions", isolate.Count);
            foreach (var member in isolate.Distinct())
                if (sessions.IsInWorld(member))
                    await CloseEntryAsync(member, capturedConnections[member]).ConfigureAwait(false);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(deliveryFailure).Throw();
        }
        return true;
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)>?>
        BossEntryResolveAttachmentsAsync(SelectedSessionIdentity requester, PartySnapshot roster, CancellationToken token)
    {
        var result = new List<(SelectedSessionIdentity, SelectCharacterResult, byte)>();
        foreach (var member in roster.Members.OrderBy(value => value.Slot))
        {
            var current = sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            var runtime = current is null ? null : await currentViews.TryLoadAsync(current, token).ConfigureAwait(false);
            if (current is null || runtime is null || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId || member.Slot > 3
                || result.Any(value => value.Item1.CharacterId == current.CharacterId
                    || value.Item1.ConnectionId == current.ConnectionId || value.Item1.WireUserId == current.WireUserId))
                return null;
            result.Add((current, runtime, member.Slot));
        }
        return result.Any(value => value.Item1 == requester) ? result : null;
    }

    private bool BossEntryCurrent(IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members,
        BossTowerSnapshot? run = null) => members.All(value =>
        sessions.IsInWorld(value.Session)
        && (run is null || bossTowers.Find(value.Session) is { } current
            && current.RunId == run.RunId && current.Room.Version == run.Room.Version));

    private bool BossEntryPartyCurrent(SelectedSessionIdentity requester, PartySnapshot expected,
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members)
    {
        var current = parties.FindParty(new PartyCharacterSnapshot(
            requester.AccountId, requester.CharacterId, requester.WireUserId, null));
        return current.Key == expected.Key
            && current.Generation == expected.Generation
            && current.Revision == expected.Revision
            && current.LeaderWireUserId == requester.WireUserId
            && current.Members.OrderBy(value => value.Slot)
                .Select(value => (value.CharacterId, value.Slot))
                .SequenceEqual(members.OrderBy(value => value.Slot)
                    .Select(value => (value.Session.CharacterId, value.Slot)));
    }

    private static void BossEntryAppendDebit(List<IGameServerMessage> frames, DungeonEntryInventoryChange debit, DateTimeOffset at)
    {
        var slot = SlotMappings.ToGameSlot(debit.Slot);
        frames.Add(debit.Remaining is null
            ? GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
                [new InventoryItemState(null, slot.Index, debit.Remaining)], at)));
    }

    private ValueTask BossEntryRefuseAsync(NetworkConnectionId connection, byte code, CancellationToken token, byte context = 0) =>
        outbox.RegisterExplicitAsync(connection,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(code, context)], []), token);

}
