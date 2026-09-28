using Dfo.Server.Data.World;
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
    DungeonDifficulty Difficulty,
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
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<AccountMaterialChange>? AccountMaterials = null);

/// <summary>
/// Keeps experience and server-authoritative quest progress on the same transaction boundary.
/// </summary>
public sealed class ApplyMonsterKillCommandHandler(
    IExperienceCalculationDomainService calculation,
    ICharacterExperienceDomainService experience,
    ICharacterQuestLogDomainService questLogs,
    ICharacterContainerDomainService containers,
    IQuestProgressDomainService questProgress,
    IItemGrantDomainService itemGrants,
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
                calculation,
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
        var granted = await PendingQuestItemGrants.GrantAsync(
            command.AccountId, command.CharacterId, command.PendingLoot, command.PartyKey,
            questLogs, containers, itemGrants, parties, cancellationToken).ConfigureAwait(false);
        var facts = new List<QuestProgressFact>
        {
            new QuestEnemyDefeated(
                command.DungeonId, command.Difficulty, command.Monster.Code, command.EnemyType,
                command.Monster.Rank),
        };
        if (command.IsFinalBossDeath && command.Monster.Rank == MonsterRank.Boss
            && command.EnemyType == HuntEnemyTargetTypes.Monster)
            facts.Add(new QuestDungeonCleared(command.DungeonId, command.Difficulty));
        if (granted.HasChanges)
            facts.Add(new QuestHoldingsChanged());
        var reported = await questProgress.ReportAsync(
            command.AccountId, command.CharacterId, facts, cancellationToken).ConfigureAwait(false);
        var advanced = reported.Changes
            .Select(change => new HuntEnemyQuestAdvance(change.QuestId, change.Before, change.After))
            .ToArray();
        return new ApplyMonsterKillResult(
            awarded,
            advanced,
            granted.CharacterItems,
            QuestProgress: reported.NotificationSnapshot,
            AccountMaterials: granted.AccountMaterials);
    }

}
