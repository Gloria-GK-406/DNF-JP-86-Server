using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

/// <summary>What was killed, as the room declared it.</summary>
/// <param name="Level">
/// The monster's level as the client was told it, with the dungeon's basis level already
/// substituted where its map declared none.
/// </param>
public readonly record struct KilledMonster(int Code, int Level, MonsterRank Rank);

/// <summary>What came of awarding a kill.</summary>
/// <param name="Award">The applied reward, or null when the character does not exist.</param>
public sealed record KillExperienceResult(ExperienceAward? Award);

/// <summary>One character's kill, awarded.</summary>
public sealed record GrantKillExperienceCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    KilledMonster Monster,
    int DungeonId,
    DungeonDifficulty Difficulty,
    double DungeonWeight,
    int PartyMemberCount = 1,
    SelectedSessionIdentity? Session = null) : ICommand<KillExperienceResult>, ISelectedSessionCommand;

/// <summary>
/// Works out what a kill was worth, adds what the character carries, and grants it.
/// </summary>
/// <remarks>
/// Three steps that each belong to something else: what the kill is worth is a rule over content,
/// what the character adds is a service over resident state, and the granting is the one
/// implementation every source of experience shares. What is here is the order and the transaction
/// around them.
/// <para>
/// Transactional because it writes the character row. Asked of every request rather than only the
/// ones that reach a write, so a mis-registration fails the first kill rather than the first kill
/// that happened to pay.
/// </para>
/// </remarks>
public sealed class GrantKillExperienceCommandHandler(
    ICharacterExperienceDomainService grant, TimeProvider clock)
    : ICommandHandler<GrantKillExperienceCommand, KillExperienceResult>
{
    public async ValueTask<KillExperienceResult> HandleAsync(
        CommandExecutionContext context,
        GrantKillExperienceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
        {
            throw new InvalidOperationException(
                "Awarding experience writes a row that outlives the process, so it has to be "
                + $"registered transactional; this execution is {context.ExecutionKind}.");
        }

        return new KillExperienceResult(
            await ApplyAwardAsync(grant, command, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false));
    }

    internal static ValueTask<ExperienceAward?> ApplyAwardAsync(
        ICharacterExperienceDomainService grant, GrantKillExperienceCommand command,
        DateTimeOffset at, CancellationToken cancellationToken) => grant.ApplyAwardAsync(
            new KillExperienceContext(
                new ExperienceOccasion(ExperienceEvent.MonsterKill, command.AccountId,
                    command.CharacterId, command.DungeonId),
                command.Monster.Level, command.Monster.Rank, command.Difficulty,
                command.DungeonWeight, command.PartyMemberCount), at, command.Session, cancellationToken);
}
