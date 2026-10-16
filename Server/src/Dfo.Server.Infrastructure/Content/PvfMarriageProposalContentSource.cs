using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageProposalContentSource(PvfArchiveProvider provider) : IMarriageProposalContentSource
{
    private readonly Lazy<MarriageProposalContent> content = new(() =>
    {
        var document = PvfDocument.Open(provider.Archive, "etc/weddingsystem.etc");
        var quests = document.Read(PvfField.Int32List("[quest]"));
        if (quests.Count != 2 || quests.Any(x => x <= 0) || quests[0] == quests[1])
            throw new InvalidDataException("Wedding content requires two distinct quest IDs.");
        return new(document.Read(PvfField.Int32("[minimum level]", minimum: 1, maximum: 255)), quests[0], quests[1]);
    });
    public MarriageProposalContent Load() => content.Value;
}
