using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Pvp;

/// <summary>Reads a duel record with the band of its grade, which only the settlement rules know.</summary>
/// <remarks>
/// The commands that show a record (character selection, the settlement's own <c>0x0030</c>) may not ask the rule
/// service themselves; this is the one place the stored record and the grade table meet for reading.
/// <para>
/// Character selection reads it on every channel, so a stored grade above the archive's top grade (a record kept from
/// before the table was enforced) must not fail the read: its band is the top grade's, the grade is shown as stored,
/// and nothing is written. Settlement starts such a record at the same band (<see cref="PvpGradeRules.Within"/>).
/// </para>
/// </remarks>
internal sealed class PvpRecordDomainService(
    IPvpRecordDomainStore records,
    IPvpSettlementRuleService rules,
    ILogger<PvpRecordDomainService> logger) : IPvpRecordDomainService
{
    public async ValueTask<PvpRecordView> ViewAsync(CharacterId character, CancellationToken cancellationToken)
    {
        var record = await records.ReadAsync(character, cancellationToken).ConfigureAwait(false);
        var band = Math.Min(record.Grade, rules.MaximumGrade);
        if (band != record.Grade)
            logger.LogWarning(
                "Character {CharacterId} has duel grade {Grade}, above the archive's top grade {MaximumGrade}; its record is shown with the top grade's band.",
                character.Value, record.Grade, band);
        var lower = rules.LowerBound(band);
        return new PvpRecordView(record.Grade, record.RatingGrade, record.Experience ?? lower, lower,
            rules.UpperBound(band), record.Wins, record.Losses);
    }
}
