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
/// <param name="Award">
/// What was awarded, or nothing when no award happened -- no such character, or a row that would
/// not take the write. Wrapped rather than returned bare because the command bus will not carry a
/// nullable result, and the distinction is the point: an award of zero still knows where the
/// character stands, and this does not.
/// </param>
public sealed record KillExperienceResult(ExperienceAward? Award);

/// <summary>One character's kill, awarded.</summary>
public sealed record GrantKillExperienceCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    KilledMonster Monster,
    int DungeonId,
    byte Difficulty,
    double DungeonWeight) : ICommand<KillExperienceResult>;

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
    IExperienceValuationDomainService valuation,
    IExperienceBonusService bonuses,
    ICharacterExperienceGrant grant)
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

        // The character's level decides what the kill is worth, and the grant is what knows it.
        // Valuing inside the grant's callback is what keeps the two from disagreeing.
        return new KillExperienceResult(
            await grant.AwardAsync(
                command.CharacterId,
                characterLevel => this.Value(command, characterLevel),
                cancellationToken).ConfigureAwait(false));
    }

    private ExperienceValuation Value(GrantKillExperienceCommand command, byte characterLevel)
    {
        var worth = valuation.WorthOfKill(
            new KillCircumstances(
                command.Monster.Level,
                command.Monster.Rank,
                characterLevel,
                command.Difficulty,
                command.DungeonWeight,
                // Solo. A party rate and a division by party size are both in the rule and both
                // exercised at one member; parties are not modelled anywhere in this server yet,
                // and a count taken from nothing would be a guess in the middle of a checked sum.
                PartyMemberCount: 1));
        if (worth == 0)
            return ExperienceValuation.Nothing;

        var occasion = new ExperienceOccasion(
            ExperienceEvent.MonsterKill,
            command.AccountId,
            command.CharacterId,
            command.DungeonId);

        // Each entitlement's amount is rounded down on its own and then added, rather than the
        // rates being summed into one multiplier. The two agree on every captured award, all of
        // which had exactly one contribution in play, and diverge only when two are; an elixir's
        // own text says such effects "overlap", which is this shape. Recorded as unsettled in the
        // TaskContract because nothing observed can yet tell them apart.
        var contributions = bonuses.Collect(occasion)
            .Select(modifier => new ExperienceContribution(
                modifier.Source, modifier.Rate, Floor(worth * modifier.Rate), modifier.Kind))
            .Where(contribution => contribution.Amount > 0)
            .ToArray();

        return new ExperienceValuation(worth, contributions);
    }

    /// <remarks>
    /// Saturating, like the one in the rule it sits beside. The rate here is an operator's number
    /// rather than the archive's, so it can be anything at all, and an unchecked cast of a double
    /// too large for a uint is undefined -- on x64 it lands on zero, turning a misconfigured
    /// enormous bonus into no bonus.
    /// </remarks>
    private static uint Floor(double value) =>
        value <= 0 || double.IsNaN(value) ? 0
        : value >= uint.MaxValue ? uint.MaxValue
        : (uint)Math.Floor(value);
}
