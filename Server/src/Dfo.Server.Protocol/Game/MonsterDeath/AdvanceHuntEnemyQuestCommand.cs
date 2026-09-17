using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

public sealed record AdvanceHuntEnemyQuestCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    int DungeonId,
    byte Difficulty,
    int EnemyCode,
    int EnemyType) : ICommand<AdvanceHuntEnemyQuestResult>;

public sealed record HuntEnemyQuestAdvance(int QuestId, uint Before, uint After);

public sealed record AdvanceHuntEnemyQuestResult(
    IReadOnlyList<HuntEnemyQuestAdvance> Advances);

/// <summary>Applies one server-verified monster death to every matching active quest target.</summary>
public sealed class AdvanceHuntEnemyQuestCommandHandler(
    IQuestProgressDomainService progress)
    : ICommandHandler<AdvanceHuntEnemyQuestCommand, AdvanceHuntEnemyQuestResult>
{
    public async ValueTask<AdvanceHuntEnemyQuestResult> HandleAsync(
        CommandExecutionContext context,
        AdvanceHuntEnemyQuestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "Hunt-enemy quest progress is persistent and requires a transactional command.");
        }

        var reported = await progress.ReportAsync(
            command.AccountId,
            command.CharacterId,
            [new QuestEnemyDefeated(
                command.DungeonId, command.Difficulty, command.EnemyCode, command.EnemyType)],
            cancellationToken).ConfigureAwait(false);
        return new AdvanceHuntEnemyQuestResult(
            [.. reported.Changes.Select(change => new HuntEnemyQuestAdvance(
                change.QuestId, change.Before, change.After))]);
    }
}
