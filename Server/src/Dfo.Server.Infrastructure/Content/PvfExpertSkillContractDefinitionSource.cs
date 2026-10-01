using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfExpertSkillContractDefinitionSource(PvfArchiveProvider provider)
    : IExpertSkillContractDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<int> TypeField = PvfField.Int32("[type]", optional: true);
        internal static readonly PvfField<int> OverSkillField = PvfField.Int32("[over skill]", optional: true);
    }

    public IReadOnlySet<int> GetOverSkillPremiumTypes()
    {
        const string path = "etc/premiumlist_new.etc";
        var archive = provider.Archive;
        var result = new HashSet<int>();
        var type = 0; var overSkill = false;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (section.Name.Equals(Fields.TypeField.Name, StringComparison.OrdinalIgnoreCase))
            {
                var value = Fields.TypeField.Decode(archive, section.Values, path);
                if (type > 0 && overSkill) result.Add(type);
                type = value; overSkill = false;
            }
            else if (section.Name.Equals(Fields.OverSkillField.Name, StringComparison.OrdinalIgnoreCase))
                overSkill |= Fields.OverSkillField.Decode(archive, section.Values, path) > 0;
        }
        if (type > 0 && overSkill) result.Add(type);
        return result;
    }
}
