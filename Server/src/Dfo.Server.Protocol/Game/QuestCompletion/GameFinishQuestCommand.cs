using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>
/// Hands the quest in and tells the client what changed because of it.
/// </summary>
/// <remarks>
/// Three frames, in the order the reference server sends them and confirmed against its 2026-08-27
/// capture: the acknowledgement, then the experience frame carrying the character's new total, then
/// the acceptable-quest list without the quest that was just finished.
/// <code>
/// S-&gt;C  CMD  0x0022  01 F8 03 04 65 00 00 00 ...   the completion
/// S-&gt;C  NOTI 0x0025  02 7B 04 00 00 ...            level 2, 1147 total
/// S-&gt;C  NOTI 0x0015  02 0A 00 65 00 ...            ten offers, 1016 gone
/// </code>
/// The acceptable list changes here where accepting a quest leaves it alone, and the difference is
/// not a contradiction: the list is computed from cleared quests among other things, and finishing
/// one is the only thing on this path that changes them.
/// </remarks>
public sealed record GameFinishQuestCommand(NetworkDispatchContext Dispatch, FinishQuestRequest Input) : IProtocolCommand<QuestFinishOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameFinishQuestCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    IQuestOfferRefreshDomainService offers,
    TimeProvider clock,
    ILogger<GameFinishQuestCommandHandler> logger,
    ITownAreaDomainService townAreas,
    IDungeonRunDomainService dungeonRuns,
    IWeaknessDomainService weakness,
    IDungeonRunProjectionDomainService runGenerations,
    Dfo.Server.Abstractions.Settlement.IQuestCompletionSettlementDomainService completion,
    IBusinessInventorySlotDomainService inventorySlots,
    IIllusionTowerDomainService? towers = null,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null)
    : ICommandHandler<GameFinishQuestCommand, QuestFinishOutcome>
{
    public async ValueTask<QuestFinishOutcome> HandleAsync(CommandExecutionContext execution, GameFinishQuestCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Finish quest {ConnectionId}: quest {Quest} with no published session",
                context.Connection.ConnectionId, message.QuestId);
            await this.AnswerAsync(context, Refusal(message.QuestId), cancellationToken)
                .ConfigureAwait(false);
            return QuestFinishOutcome.Refuse(message.QuestId);
        }

        var request = execution.Request as CommandRequestContext.ProtocolEffectSelected
            ?? throw new InvalidOperationException("Quest completion requires its exact selected request.");
        if (request.Recipient.Session != session)
            throw new InvalidOperationException("Quest completion changed its selected actor.");
        var input = new QuestCompletionInput(session.AccountId, session.CharacterId, message.QuestId,
            message.RewardSelection, message.CompletionCount,
            checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()), session);
        QuestFinishRejectedException? rejected = null;
        var outcome = await units.WriteAsync(execution,
            new("FinishQuest", [request.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try { return await CompleteAsync(input, unit.Footprint, token).ConfigureAwait(false); }
                catch (QuestFinishRejectedException exception) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(exception))
                {
                    unit.RequireRollback(exception);
                    rejected = exception;
                    return QuestFinishOutcome.Refuse(message.QuestId);
                }
            }, cancellationToken).ConfigureAwait(false);

        if (rejected is not null)
            logger.LogWarning(rejected, "Finish quest {ConnectionId}: quest {Quest} settlement rolled back",
                context.Connection.ConnectionId, message.QuestId);

        logger.LogInformation(
            "Finish quest {ConnectionId} account={Account}: quest={Quest} {Verdict} "
                + "exp={Experience} items={Items} level={Level} total={Total} grow={GrowType} awakening={Awakening}",
            context.Connection.ConnectionId,
            session.AccountId,
            message.QuestId,
            outcome.Finished ? "finished" : $"refused({outcome.Refusal})",
            outcome.Experience,
            outcome.Items.Count,
            outcome.NewLevel,
            outcome.NewExperience, outcome.GrowType, outcome.Awakening);

        if (outcome.Finished && outcome.GrowType is not null)
        {
            result = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Character left before advancement could be projected.");
            await outbox.RegisterExplicitAsync(context.Connection.ConnectionId,
                new GameExplicitMessageDelivery([new SkillStateMessage(result.CharacterEntry.Skills)], []),
                cancellationToken).ConfigureAwait(false);
        }

        await this.AnswerAsync(
            context,
            new FinishQuestAcknowledgement(
                outcome.Finished,
                outcome.QuestId,
                outcome.ObjectiveKind,
                outcome.Experience,
                outcome.Items,
                checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()),
                (byte)(outcome.Refusal ?? QuestFinishRefusal.Unusable), outcome.GrowType)
                { Awakening = outcome.Awakening, SkillPages = result.CharacterEntry.Skills.Pages },
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Finished)
        {
            if (outcome.QuestProgress is { } refusedProgress)
            {
                await outbox.RegisterExplicitAsync(
                    context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery([QuestProgressMessage(refusedProgress)], []),
                    cancellationToken).ConfigureAwait(false);
            }
            return outcome;
        }

        if (outcome.MirrorCompletion)
        {
            var mirrorFrames = new List<IGameServerMessage>();
            mirrorFrames.AddRange(GameInventoryDelivery.Notifications(outcome.ConsumedItemUpdates));
            if (outcome.AccountMaterials is { Count: > 0 } materials)
                mirrorFrames.Add(GameInventoryDelivery.VirtualResourceNotification(materials, clock.GetUtcNow()));
            if (outcome.QuestProgress is { } mirrorProgress) mirrorFrames.Add(QuestProgressMessage(mirrorProgress));
            if (mirrorFrames.Count != 0)
                await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new GameExplicitMessageDelivery(mirrorFrames, []), cancellationToken).ConfigureAwait(false);
            return outcome;
        }

        // The session is corrected before the frames go out, and the correction is the fix for a
        // real defect rather than tidiness. Every later frame reads the character and the offer
        // list from here: a session still holding the entry-time list re-sends the finished quest
        // as offerable the next time anything refreshes it, and a session still holding the
        // entry-time experience prices the next completion at the wrong level. Both were seen on
        // 2026-08-27 -- 与赛丽亚的初次见面 was offered again after being handed in, and the
        // second completion wrote the same total as the first.
        result = result with
        {
            Character = result.Character with
            {
                Progression = new CharacterProgressionState(
                    outcome.NewLevel, outcome.NewExperience),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    Extras = result.CharacterEntry.Public.Extras with
                    {
                        CharacterExperience = outcome.NewExperience,
                    },
                },
            },
        };
        // Worked out again rather than edited. Handing a quest in can satisfy another quest's
        // prerequisite, and the quest that becomes offerable because of it has to APPEAR -- an
        // edited list can only shrink. Quest 1776 declares [pre required quest] 101, so finishing
        // 青之守护者塔娜 is what makes it offerable; with a shrinking list the player was left with
        // no next quest and no way into a dungeon until they logged out.
        result = await offers.RefreshAsync(
            result,
            session.AccountId,
            session.CharacterId,
            outcome.NewLevel,
            cancellationToken).ConfigureAwait(false);

        // Asked of the character, and asked after the completion has been written: handing a quest
        // in can cross a level, and a level grants more points to spend. The session's own copy is
        // entry's projection of the moment the character walked in and nothing recomputes it, so
        // reading it here would report the points the character had before the quest -- which is
        // what it did until now.
        var mirror = await characters.Get(session.CharacterId)
            .RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
        var frames = new List<IGameServerMessage>();
        frames.AddRange(GameInventoryDelivery.Notifications(outcome.ConsumedItemUpdates));
        if (outcome.AccountMaterials is { Count: > 0 } accountMaterials)
        {
            frames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [.. accountMaterials.Select(material => new InventoryRuntimeValueState(
                    material.SlotIndex,
                    material.ItemId,
                    material.Value))],
                clock.GetUtcNow()));
        }
        if (mirror is not null)
        {
            frames.Add(new ExperienceNotification(
                outcome.NewLevel, outcome.NotificationExperience, mirror, AccountHonorLevel.Initial,
                // A quest pays its own table's figure and nothing on top, so there is no named
                // line to declare. The client draws the whole gain as the base, which is what it
                // is.
                ExperienceBonusLines.None));
        }
        else
        {
            // The frame carries the character's remaining skill points as well as the total, and
            // inventing a figure would tell the client it had points it does not. The completion
            // still stands.
            logger.LogWarning(
                "Finish quest {ConnectionId}: quest {Quest} was finished but the character has "
                    + "no skill point ledger to report, so the experience frame was not sent",
                context.Connection.ConnectionId, message.QuestId);
        }

        var targets = new List<GameExplicitMessageTarget>();
        if (outcome.GrowType is not null)
        {
            var equipment = result.CharacterInventory.Containers
                .SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
            var appearance = new CurrentUserStateMessage(result.Character, result.CharacterEntry.Public,
                equipment, result.CharacterEntry.Features.Pets);
            frames.Add(appearance);
            frames.Add(new ExtendedUserStateMessage(result.Character, result.CharacterEntry.Public,
                result.CharacterEntry.Skills, equipment, clock.GetUtcNow()));
            var audience = new HashSet<SelectedSessionIdentity>();
            if (selectedSessions.IsInWorld(session))
            {
                if (towers?.Find(session) is { } tower)
                {
                    foreach (var member in tower.Members)
                    {
                        var peer = member.Session;
                        if (selectedSessions.IsInWorld(peer)
                            && towers.Find(peer)?.RunId == tower.RunId)
                            audience.Add(peer);
                    }
                }
                else
                {
                    if (townAreas.Find(session.CharacterId) is { } own && session.Owns(own)
                        && townAreas.Find(own.Area) is { } area)
                        foreach (var member in area.VisibleTo(session.CharacterId))
                            if (selectedSessions.TryGetInWorld(member.CharacterId, out var peer) && peer.Owns(member))
                                audience.Add(peer);
                    if (dungeonRuns.Find(session.CharacterId) is { } run)
                    {
                        var admitted = runGenerations.Find(run.RunId);
                        if (admitted.Contains(session))
                            foreach (var peer in admitted)
                                if (selectedSessions.IsInWorld(peer)
                                    && dungeonRuns.Find(peer.CharacterId)?.RunId == run.RunId)
                                    audience.Add(peer);
                    }
                }
            }
            foreach (var peer in audience.Where(peer => peer != session))
                targets.Add(new(peer.ConnectionId, [appearance]));
            // Special completion carries the career instead of the ordinary item list.
            var inventory = result.CharacterInventory.Containers
                .SingleOrDefault(container => container.ContainerType == InventoryContainerType.Main);
            if (inventory is not null)
                frames.Add(new InventorySnapshotMessage(inventory, result.CharacterInventory.VirtualResources,
                    result.AccountInventory.VirtualResources, result.AccountInventory.Cargo.SelectionKey,
                    result.AccountInventory.Cargo.Value32, clock.GetUtcNow()));
        }

        frames.Add(new AvailableQuestListMessage(
            outcome.NewLevel, result.World.Content.AvailableQuests));

        if (outcome.QuestProgress is { } progress)
        {
            frames.Add(QuestProgressMessage(progress));
        }

        if (outcome.GrowType is not null) frames.AddRange(await HousingCurrentProjection.ReadAsync(housing, session, cancellationToken).ConfigureAwait(false));
        await outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, targets),
            cancellationToken).ConfigureAwait(false);
        if (outcome.GrowType is not null && selectedSessions.IsInWorld(session))
            await outbox.RegisterNotiAsync(session.ConnectionId,
                new WeaknessProjectionNotification(weakness.For(session.CharacterId).Value), cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private static FinishQuestAcknowledgement Refusal(ushort questId) =>
        new(false, questId, 0, 0, [], 0, (byte)QuestFinishRefusal.Unusable);

    private static AcceptedQuestProgressMessage QuestProgressMessage(QuestProgressSnapshot progress) =>
        new([.. progress.Quests.OrderBy(quest => quest.Slot)
            .Select(quest => new AcceptedQuestProgressState(
                checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]);

    private ValueTask AnswerAsync(
        NetworkDispatchContext context,
        FinishQuestAcknowledgement acknowledgement,
        CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([acknowledgement], []),
            cancellationToken);

    internal async ValueTask<QuestFinishOutcome> CompleteAsync(
        QuestCompletionInput command, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            var result = await completion.FinishAsync(command.AccountId, command.CharacterId, command.QuestId,
                command.RewardSelection, command.CompletionCount, DateTimeOffset.FromUnixTimeSeconds(command.AcquiredAtUnixTime), footprint, cancellationToken, command.Session).ConfigureAwait(false);
            var isMirror = result.MirrorCompletion;
            if (!result.Finished) return QuestFinishOutcome.Refuse(command.QuestId, result.Projection);
            var slots = new InventorySlotMappingRuleService();
            var items = new List<FinishedQuestItem>();
            var account = new List<InventoryRuntimeValueState>();
            var consumed = new HashSet<BusinessSlot>();
            foreach (var applied in result.Applied!.Applied)
                switch (applied.Change)
                {
                    case ContainerConsumptionChange change:
                        consumed.Add(change.Slot);
                        break;
                    case Dfo.Server.Abstractions.Effects.BasicCharacterItemChange basic:
                        items.AddRange(basic.Additions.Select(x => new FinishedQuestItem(checked((ushort)slots.ToGameSlot(x.Slot).Index),
                            (uint)((Dfo.Server.Abstractions.Effects.GrantBasicCharacterItemEffect)applied.Effect).ItemId, checked((uint)x.Count))));
                        foreach (var addition in basic.Additions.Where(x => x.Slot.Space == BusinessInventorySpace.SharedMaterial))
                        {
                            var bound = inventorySlots.Find(command.CharacterId, addition.Slot)!;
                            var held = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false)
                                ?? throw new InvalidDataException("Shared reward disappeared before commit.");
                            account.Add(new(slots.ToGameSlot(addition.Slot).Index, held.ItemId, held.Count));
                        }
                        break;
                    case Dfo.Server.Abstractions.Effects.QuestRewardGoldChange gold when gold.After > gold.Before:
                        items.Add(new(0, 0, checked((uint)(gold.After - gold.Before))));
                        break;
                }
            var updates = new List<CommittedInventoryUpdateState>();
            if (result.Applied.Applied.Any(x => x.Change is Dfo.Server.Abstractions.Effects.GoldEffectChange))
            {
                var balance = result.Applied.Applied.Select(x => x.Change).Select(x => x switch
                {
                    Dfo.Server.Abstractions.Effects.GoldEffectChange spent => (int?)spent.Remaining,
                    Dfo.Server.Abstractions.Effects.QuestRewardGoldChange paid => paid.After,
                    _ => null,
                }).Last(x => x.HasValue)!.Value;
                updates.Add(new(InventoryContainerType.Main, [new(null, 0, StackableItemCore.Create(0, 0, balance))],
                    DateTimeOffset.FromUnixTimeSeconds(command.AcquiredAtUnixTime)));
            }
            if (isMirror)
                foreach (var addition in result.Applied.Applied.Select(x => x.Change).OfType<Dfo.Server.Abstractions.Effects.BasicCharacterItemChange>().SelectMany(x => x.Additions))
                    consumed.Add(addition.Slot);
            foreach (var slot in consumed)
            {
                var bound = inventorySlots.Find(command.CharacterId, slot) ?? throw new InvalidDataException("Consumed quest slot vanished.");
                var item = await bound.Container.GetAsync(bound.Slot.Index, cancellationToken).ConfigureAwait(false);
                var wire = slots.ToGameSlot(slot);
                updates.Add(new(wire.Space, [new(item?.ItemUid, wire.Index,
                    item?.Snapshot.ToCore() ?? StackableItemCore.Create(0, -1, 0))], DateTimeOffset.FromUnixTimeSeconds(command.AcquiredAtUnixTime)));
            }
            return new(true, command.QuestId, ObjectiveKindOf(result.Objective), result.Award!.Gained, items,
                result.Award.Level, result.Award.Total, QuestProgress: result.Projection, AccountMaterials: account, GrowType: result.GrowType)
            { MirrorCompletion = isMirror, Awakening = result.Awakening, NotificationExperience = result.Award.NotificationTotal, ConsumedItemUpdates = updates };
        }
        catch (Exception exception) when ((exception is not (QuestFinishRejectedException or OperationCanceledException)) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(exception))
        {
            throw new QuestFinishRejectedException(command.QuestId, "Quest completion settlement was rejected.", exception);
        }
    }
    private static byte ObjectiveKindOf(string? objectiveTypeCode) =>
        objectiveTypeCode?.Trim().ToLowerInvariant() switch
        {
            "seeking" => 0x00,
            "condition under clear" => 0x01,
            "hunt monster" => 0x02,
            "meet npc" => 0x04,
            "hunt enemy" => 0x06,
            "custom quest" => 0x0A,
            "use item" => 0x0B,
            _ => 0x00,
        };
}
