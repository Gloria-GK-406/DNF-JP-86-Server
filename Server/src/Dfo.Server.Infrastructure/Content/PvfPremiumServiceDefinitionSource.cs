using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPremiumServiceDefinitionSource(PvfArchiveProvider provider) : IPremiumServiceDefinitionSource
{
    public PremiumContractLevels ReadContractLevels()
    {
        var type = 0; var skill = 0; var equipment = 0;
        foreach (var section in PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/premiumlist_new.etc"))
        {
            if (section.Values.Any(value => value.Kind != PvfTokenKind.Int32)) continue;
            var values = section.Values.Select(value => value.Value).ToArray();
            if (section.Name == "[type]" && values.Length == 1) type = values[0];
            if (type == 27 && section.Name == "[over skill]" && values.Length == 1) skill = values[0];
            if (type == 22 && section.Name == "[over equipinfo]" && values.Length > 0)
            {
                if (values[0] <= 0 || values.Length != 1 + 2 * values[0])
                    throw new InvalidDataException("Malformed over-equipment contract declaration.");
                var allowances = values.Skip(2).Where((_, index) => index % 2 == 0).Distinct().ToArray();
                // A21 type 22 declares the same level allowance for all five categories.
                if (allowances.Length != 1) throw new InvalidDataException("Category-specific equipment allowances require an explicit mapping.");
                equipment = allowances[0];
            }
        }
        if (skill <= 0 || equipment <= 0) throw new InvalidDataException("Missing contract level allowances.");
        return new(skill, equipment);
    }
}
