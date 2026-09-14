using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

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
    int PartyMemberCount = 1) : ICommand<ApplyMonsterKillResult>;

public sealed record ApplyMonsterKillResult(
    ExperienceAward? Award,
    IReadOnlyList<HuntEnemyQuestAdvance> QuestAdvances);

/// <summary>
/// Keeps experience and server-authoritative quest progress on the same transaction boundary.
/// </summary>
public sealed class ApplyMonsterKillCommandHandler(
    IExperienceValuationDomainService valuation,
    IExperienceBonusDomainService bonuses,
    ICharacterExperienceDomainService experience,
    IQuestDefinitionDomainService questDefinitions,
    ICharacterQuestLogDomainService questLogs)
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
        var advanced = await AdvanceHuntEnemyQuestCommandHandler.AdvanceAsync(
            questDefinitions,
            questLogs,
            new AdvanceHuntEnemyQuestCommand(
                command.AccountId,
                command.CharacterId,
                command.DungeonId,
                command.Difficulty,
                command.Monster.Code,
                command.EnemyType),
            cancellationToken).ConfigureAwait(false);

        return new ApplyMonsterKillResult(awarded, advanced);
    }
}
