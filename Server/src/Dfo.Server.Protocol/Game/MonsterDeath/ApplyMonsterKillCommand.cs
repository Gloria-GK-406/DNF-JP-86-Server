using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

/// <summary>One canonical kill's durable effects, committed as one transaction.</summary>
public sealed record ApplyMonsterKillCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    KilledMonster Monster,
    int DungeonId,
    byte Difficulty,
    double DungeonWeight,
    int EnemyType = HuntEnemyTargetTypes.Monster,
    int PartyMemberCount = 1,
    IReadOnlyList<RolledDrop>? PendingLoot = null,
    PartyKey PartyKey = default,
    bool IsFinalBossDeath = false) : ICommand<ApplyMonsterKillResult>;

public sealed record ApplyMonsterKillResult(
    ExperienceAward? Award,
    IReadOnlyList<HuntEnemyQuestAdvance> QuestAdvances,
    CommittedInventoryUpdateState? QuestItems = null,
    QuestProgressSnapshot? QuestProgress = null);

/// <summary>
/// Keeps experience and server-authoritative quest progress on the same transaction boundary.
/// </summary>
public sealed class ApplyMonsterKillCommandHandler(
    IExperienceValuationDomainService valuation,
    IExperienceBonusDomainService bonuses,
    ICharacterExperienceDomainService experience,
    ICharacterQuestLogDomainService questLogs,
    ICharacterContainerDomainService containers,
    IQuestProgressDomainService questProgress,
    IPartyService? parties = null)
    : ICommandHandler<ApplyMonsterKillCommand, ApplyMonsterKillResult>
{
    public async ValueTask<ApplyMonsterKillResult> HandleAsync(
        CommandExecutionContext context,
        ApplyMonsterKillCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "A canonical monster kill changes multiple durable ledgers and requires one "
                    + "transaction.");
        }

        var awarded = command.EnemyType == HuntEnemyTargetTypes.Monster
            ? await GrantKillExperienceCommandHandler.AwardAsync(
                valuation,
                bonuses,
                experience,
                new GrantKillExperienceCommand(
                    command.AccountId,
                    command.CharacterId,
                    command.Monster,
                    command.DungeonId,
                    command.Difficulty,
                    command.DungeonWeight,
                    command.PartyMemberCount),
                cancellationToken).ConfigureAwait(false)
            : null;
        var granted = await GrantQuestItemsAsync(command, cancellationToken).ConfigureAwait(false);
        var facts = new List<QuestProgressFact>
        {
            new QuestEnemyDefeated(
                command.DungeonId, command.Difficulty, command.Monster.Code, command.EnemyType,
                command.Monster.Rank),
        };
        if (command.IsFinalBossDeath && command.Monster.Rank == MonsterRank.Boss
            && command.EnemyType == HuntEnemyTargetTypes.Monster)
            facts.Add(new QuestDungeonCleared(command.DungeonId, command.Difficulty));
        if (granted is not null)
            facts.Add(new QuestHoldingsChanged());
        var reported = await questProgress.ReportAsync(
            command.AccountId, command.CharacterId, facts, cancellationToken).ConfigureAwait(false);
        var advanced = reported.Changes
            .Select(change => new HuntEnemyQuestAdvance(change.QuestId, change.Before, change.After))
            .ToArray();
        return new ApplyMonsterKillResult(
            awarded, advanced, granted, QuestProgress: reported.NotificationSnapshot);
    }

    private async ValueTask<CommittedInventoryUpdateState?> GrantQuestItemsAsync(
        ApplyMonsterKillCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PartyKey != default
            && (parties?.FindByKey(command.PartyKey) is not { } current
                || !current.Members.Any(member => member.CharacterId == command.CharacterId)))
            return null;
        var candidates = command.PendingLoot?
            .Where(drop => drop.Delivery == RolledDropDelivery.DirectQuestItem
                && drop.OwnerCharacterId == command.CharacterId)
            .ToArray() ?? [];
        if (candidates.Length == 0)
            return null;

        var bag = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (bag is null)
            return null;
        var carried = await questLogs.LogOf(command.AccountId, command.CharacterId)
            .CarriedAsync(cancellationToken).ConfigureAwait(false);
        var active = carried.Select(quest => quest.QuestId).ToHashSet();
        var changed = new HashSet<int>();
        foreach (var drop in candidates)
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId)
                || drop.ItemId <= 0 || drop.Count <= 0 || drop.GrantLimit <= 0)
                continue;

            var held = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == drop.ItemId)
                .Sum(item => (long)item.Count);
            var count = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            if (count <= 0)
                continue;
            var remaining = count;
            while (remaining > 0)
            {
                var placement = await bag.AddAsync(drop.ItemId, remaining, cancellationToken)
                    .ConfigureAwait(false);
                switch (placement)
                {
                    case ItemPlacementOutcome.AddedToPile topped when topped.Added > 0:
                        changed.Add(topped.SlotIndex);
                        remaining -= topped.Added;
                        break;
                    case ItemPlacementOutcome.PutInEmptySlot opened when opened.Count > 0:
                        changed.Add(opened.SlotIndex);
                        remaining -= opened.Count;
                        break;
                    case ItemPlacementOutcome.NoRoom:
                        remaining = 0;
                        break;
                    case ItemPlacementOutcome.UnknownItem:
                        throw new InvalidDataException($"Quest item {drop.ItemId} has no inventory definition.");
                    default:
                        throw new InvalidDataException(
                            $"Quest item {drop.ItemId} placement made no progress: {placement}.");
                }
            }
        }

        if (changed.Count == 0)
            return null;
        var items = new List<InventoryItemState>(changed.Count);
        foreach (var slot in changed.Order())
        {
            var item = await bag.ReadSlotAsync(slot, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Quest item slot {slot} disappeared before commit.");
            items.Add(new InventoryItemState(null, slot, item.Core));
        }
        return new CommittedInventoryUpdateState(
            InventoryContainerType.Main, items, DateTimeOffset.UtcNow);
    }
}
