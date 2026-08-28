using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>What came of paying for a finished run.</summary>
/// <param name="Award">
/// What was awarded and where the character now stands, or nothing when there was no character to
/// award to. The same distinction a kill draws: an award of nothing still knows the character's
/// standing, and this does not.
/// </param>
/// <param name="ClearBase">What the clear itself was worth, for the result screen to report.</param>
/// <param name="RankBonus">What the run's rank added, likewise.</param>
public sealed record ClearExperienceResult(
    ExperienceAward? Award, uint ClearBase, uint RankBonus);

/// <summary>One character's finished run, paid.</summary>
public sealed record GrantClearExperienceCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    int DungeonId,
    ClearCircumstances Clear) : ICommand<ClearExperienceResult>;

/// <summary>
/// Works out what a finished run was worth and grants it.
/// </summary>
/// <remarks>
/// The same shape as the kill's: a rule over content decides the amount, the shared grant adds it,
/// and the transaction is here. What it deliberately does not do is ask
/// <c>IExperienceBonusService</c> what the character carries.
/// <para>
/// <b>Why nothing is collected here.</b> A settlement is drawn on the result screen, whose
/// breakdown this server fills from the two figures below. An entitlement paid at settlement would
/// reach the character's total but appear in none of that screen's fields -- the twenty-three the
/// frame carries are unmeasured in A21, and the reference server's answer for the channel's own
/// belongs to a frame with a different number of them. Paying what cannot be declared leaves the
/// screen short by the difference, and this whole subject has already cost a day to exactly that
/// kind of silent gap. The measurement that would settle it is written up in this Spec.
/// </para>
/// </remarks>
public sealed class GrantClearExperienceCommandHandler(
    IQuestParameterSource questParameters,
    IExperienceParameterSource parameters,
    IDungeonRankGradeSource rankGrades,
    ICharacterExperienceGrant grant)
    : ICommandHandler<GrantClearExperienceCommand, ClearExperienceResult>
{
    public async ValueTask<ClearExperienceResult> HandleAsync(
        CommandExecutionContext context,
        GrantClearExperienceCommand command,
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

        var settings = parameters.Load();
        var clearBase = DungeonClearExperienceRules.BaseWorth(
            command.Clear, questParameters.Load(), settings);
        var rankBonus = DungeonClearExperienceRules.RankBonus(
            clearBase, command.Clear.ClientRankPoint, rankGrades.Load(), settings);

        // The two figures are worked out before the grant rather than inside its callback, because
        // neither depends on the character's level -- unlike a kill, where the level decides the
        // gap penalty and reading it twice is how a stale copy gets in.
        var award = await grant.AwardAsync(
            command.CharacterId,
            _ => clearBase == 0
                ? ExperienceValuation.Nothing
                : new ExperienceValuation(
                    clearBase,
                    rankBonus == 0
                        ? []
                        // Unlisted: the experience notice has no line named for a clear's rank, so
                        // the client draws this as part of what the clear itself paid -- which is
                        // what it is. The result screen reports it on its own field.
                        : [new ExperienceContribution(
                            "clear rank", RateOf(clearBase, rankBonus), rankBonus)]),
            cancellationToken).ConfigureAwait(false);

        return new ClearExperienceResult(award, clearBase, rankBonus);
    }

    /// <summary>
    /// The proportion the rank bonus came to, for the log that has to explain the award afterwards.
    /// </summary>
    /// <remarks>
    /// Recovered from the two amounts rather than carried down from the table, so it reports what
    /// was actually paid rather than what was meant to be.
    /// </remarks>
    private static double RateOf(uint clearBase, uint rankBonus) =>
        clearBase == 0 ? 0 : (double)rankBonus / clearBase;
}
