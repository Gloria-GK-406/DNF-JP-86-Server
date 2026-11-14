using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

/// <summary>How one battle moves a fighter's grade experience.</summary>
internal static class PvpGradeRules
{
    public static PvpGradeStep Step(PvpGradeCatalog catalog, byte grade) =>
        grade <= catalog.MaximumGrade
            ? catalog.Grades[grade]
            : throw new ArgumentOutOfRangeException(nameof(grade), grade, "Grade is above the archive's top grade.");

    /// <summary>
    /// A stored grade the archive's table can answer for: one above the top grade (kept from before the table was
    /// enforced) reads as the top grade. The stored value itself is left alone.
    /// </summary>
    public static byte Within(PvpGradeCatalog catalog, byte grade) => Math.Min(grade, catalog.MaximumGrade);

    /// <summary>The grade an amount of experience falls in; experience past the top grade's bound stays at the top grade.</summary>
    public static byte GradeOf(PvpGradeCatalog catalog, long experience)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(experience);
        var grade = (byte)0;
        while (grade < catalog.MaximumGrade && experience >= catalog.Grades[grade + 1].LowerBound) grade++;
        return grade;
    }

    public static PvpSettlementDecision Decide(PvpGradeCatalog catalog, PvpSettlementFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var experience = facts.Experience ?? Step(catalog, Within(catalog, facts.Grade)).LowerBound;
        var grade = GradeOf(catalog, experience);
        if (!facts.Entitlements.RecordsResult) return new(grade, experience, 0, false);

        var outcome = facts.Escaped ? PvpOutcome.Loss : facts.Outcome;
        var change = facts.Entitlements.AwardsGradeExperience
            ? Change(catalog, facts, outcome, grade, experience)
            : 0;
        var next = experience + change;
        return new(GradeOf(catalog, next), next, change, outcome != PvpOutcome.Draw);
    }

    private static long Change(PvpGradeCatalog catalog, PvpSettlementFacts facts, PvpOutcome outcome, byte grade,
        long experience) => outcome switch
    {
        PvpOutcome.Win when facts.WinsEarlierToday >= facts.Entitlements.DailyWinExperienceCap => 0,
        PvpOutcome.Win when grade == catalog.MaximumGrade => 0,
        // Grade 0 has no climb of its own: the first counted win places the fighter at grade 1.
        PvpOutcome.Win when grade == 0 => Step(catalog, 1).LowerBound - experience,
        PvpOutcome.Win => Step(catalog, grade).WinExperience,
        PvpOutcome.Loss when facts.LossesEarlierToday >= facts.Entitlements.DailyLossExperienceCap => 0,
        PvpOutcome.Loss => Math.Max(Step(catalog, grade).LossExperience, -experience),
        PvpOutcome.Draw => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
