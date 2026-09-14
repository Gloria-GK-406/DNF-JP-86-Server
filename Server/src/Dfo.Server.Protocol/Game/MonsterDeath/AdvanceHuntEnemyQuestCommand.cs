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
    IQuestDefinitionDomainService quests,
    ICharacterQuestLogDomainService logs)
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

        return new AdvanceHuntEnemyQuestResult(
            await AdvanceAsync(quests, logs, command, cancellationToken).ConfigureAwait(false));
    }

    internal static async ValueTask<IReadOnlyList<HuntEnemyQuestAdvance>> AdvanceAsync(
        IQuestDefinitionDomainService quests,
        ICharacterQuestLogDomainService logs,
        AdvanceHuntEnemyQuestCommand command,
        CancellationToken cancellationToken)
    {
        var log = logs.LogOf(command.AccountId, command.CharacterId);
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var advances = new List<HuntEnemyQuestAdvance>();
        foreach (var active in carried)
        {
            var definition = await quests.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition is null || definition.HuntEnemyRequirements.Count == 0)
                continue;

            var current = new QuestTrigger(unchecked((uint)active.TriggerValue));
            var next = current;
            var count = Math.Min(
                definition.HuntEnemyRequirements.Count,
                QuestTrigger.ChannelCount);
            for (var index = 0; index < count; index++)
            {
                var target = definition.HuntEnemyRequirements[index];
                if (Matches(target, command))
                {
                    next = next.ReplaceChannel(index, next.GetChannel(index) - 1L);
                }
            }

            if (next == current)
                continue;

            if (await log.RecordProgressAsync(
                    active,
                    unchecked((int)next.PackedValue),
                    cancellationToken).ConfigureAwait(false))
            {
                advances.Add(new HuntEnemyQuestAdvance(
                    active.QuestId,
                    current.PackedValue,
                    next.PackedValue));
            }
        }

        return advances;
    }

    private static bool Matches(
        Dfo.Server.Abstractions.Content.HuntEnemyRequirement target,
        AdvanceHuntEnemyQuestCommand command) =>
        target.DungeonId == command.DungeonId
        && (target.Difficulty < 0 || target.Difficulty == command.Difficulty)
        && target.EnemyCode == command.EnemyCode
        && target.EnemyType == command.EnemyType;
}
