using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfExpertSkillContractDefinitionSource(PvfArchiveProvider provider)
    : IExpertSkillContractDefinitionSource
{
    public IReadOnlySet<int> GetOverSkillPremiumTypes()
    {
        var result = new HashSet<int>(); var reader = provider.Archive.OpenScript("etc/premiumlist_new.etc");
        var type = 0; var overSkill = false;
        while (reader.Read(out var token))
        {
            if (token.Kind != PvfTokenKind.Identifier) continue;
            var tag = provider.Archive.ResolveString(token.Value);
            if (tag.Equals("[type]", StringComparison.OrdinalIgnoreCase) && reader.TryReadInt32(out var value))
            { if (type > 0 && overSkill) result.Add(type); type = value; overSkill = false; }
            else if (tag.Equals("[over skill]", StringComparison.OrdinalIgnoreCase)
                     && reader.TryReadInt32(out var level) && level > 0) overSkill = true;
        }
        if (type > 0 && overSkill) result.Add(type);
        return result;
    }
}
