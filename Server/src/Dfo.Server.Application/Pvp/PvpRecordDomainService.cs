using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

/// <summary>Reads a duel record with the band of its grade, which only the settlement rules know.</summary>
/// <remarks>
/// The commands that show a record (character selection, the settlement's own <c>0x0030</c>) may not ask the rule
/// service themselves; this is the one place the stored record and the grade table meet for reading.
/// </remarks>
internal sealed class PvpRecordDomainService(IPvpRecordDomainStore records, IPvpSettlementRuleService rules) : IPvpRecordDomainService
{
    public async ValueTask<PvpRecordView> ViewAsync(CharacterId character, CancellationToken cancellationToken)
    {
        var record = await records.ReadAsync(character, cancellationToken).ConfigureAwait(false);
        var lower = rules.LowerBound(record.Grade);
        return new PvpRecordView(record.Grade, record.RatingGrade, record.Experience ?? lower, lower,
            rules.UpperBound(record.Grade), record.Wins, record.Losses);
    }
}
