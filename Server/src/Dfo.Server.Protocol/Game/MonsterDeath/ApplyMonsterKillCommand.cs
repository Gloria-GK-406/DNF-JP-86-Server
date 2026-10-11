using Dfo.Server.Data.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Abstractions.Achievements;

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
    bool IsFinalBossDeath = false,
    SelectedSessionIdentity? Session = null, DungeonRunId? RunId = null, RoomActorId? Actor = null,
    int? WorldMapId = null) : ICommand<ApplyMonsterKillResult>, ISelectedSessionCommand;

public sealed record ApplyMonsterKillResult(
    ExperienceAward? Award,
    IReadOnlyList<HuntEnemyQuestAdvance> QuestAdvances,
    CommittedInventoryUpdateState? QuestItems = null,
    QuestProgressSnapshot? QuestProgress = null,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null);

/// <summary>
/// Keeps experience and server-authoritative quest progress on the same transaction boundary.
/// </summary>
public sealed class ApplyMonsterKillCommandHandler(
    IMonsterKillRewardsDomainService rewards)
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

        var applied = await rewards.ApplyAsync(
            command.Session, command.AccountId, command.CharacterId,
            new IdentifiedKill(command.Monster.Code, command.Monster.Level, command.Monster.Rank,
                command.EnemyType == HuntEnemyTargetTypes.Monster, command.Monster.IsNamedMonster),
            command.DungeonId, command.Difficulty, command.DungeonWeight, command.PartyMemberCount,
            command.PartyKey, command.IsFinalBossDeath, command.RunId, command.Actor,
            command.WorldMapId, command.PendingLoot, cancellationToken).ConfigureAwait(false);
        return new(applied.Award, applied.QuestChanges.Select(x => new HuntEnemyQuestAdvance(x.QuestId, x.Before, x.After)).ToArray(), applied.QuestItems, applied.QuestProgress, applied.AccountMaterials);
    }
}
