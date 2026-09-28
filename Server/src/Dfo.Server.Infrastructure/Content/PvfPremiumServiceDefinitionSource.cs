using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPremiumServiceDefinitionSource(PvfArchiveProvider provider) : IPremiumServiceDefinitionSource, IQuestHelperPuppetDefinitionSource
{
    public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() =>
        Array.AsReadOnly(ReadQuestHelperValues().Skip(1).Take(3).ToArray());

    public IReadOnlyList<int> ReadPuppetItemIds()
    {
        var ids = ReadQuestHelperValues().Skip(4).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Duplicate quest helper puppet IDs.");
        return Array.AsReadOnly(ids);
    }

    private int[] ReadQuestHelperValues()
    {
        var section = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/chn_characpremium.etc")
            .Single(s => s.Name == "[quest_helper]");
        // A21 stores an unknown shared prefix, three effect values, then four doll IDs.
        // Interpreting the effect values as per-attempt floors is our adopted server policy.
        if (section.Values.Count != 8 || section.Values.Any(v => v.Kind != PvfTokenKind.Int32)
            || section.Values.Skip(1).Take(3).Any(v => v.Value is < 0 or > 100)
            || section.Values.Skip(4).Any(v => v.Value <= 0))
            throw new InvalidDataException("Invalid quest helper rules.");
        return section.Values.Select(v => v.Value).ToArray();
    }

    public DoublePotRules ReadDoublePotRules()
    {
        var section = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/chn_characpremium.etc")
            .Single(s => s.Name == "[lottery_onemore]");
        if (section.Values.Count < 3 || section.Values.Any(v => v.Kind != PvfTokenKind.Int32)
            || section.Values[1].Value is < 1 or > 255 || section.Values.Skip(2).Any(v => v.Value <= 0))
            throw new InvalidDataException("Invalid double pot rules.");
        return new(section.Values[1].Value, section.Values.Skip(2).Select(v => v.Value).ToHashSet());
    }
    public int ReadFreeWeaknessDailyLimit() => ReadDailyLimit("[free_stamina]");
    public int ReadFreeGoldCardDailyLimit() => ReadDailyLimit("[plus_goldcard]");
    public int ReadDungeonBuffDailyLimit() => ReadDailyLimit("[buff_service]", 23);
    public int ReadAutoRepairDailyLimit() => ReadDailyLimit("[auto_repair]");
    private int ReadDailyLimit(string name, int valueCount = 2)
    {
        var section = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/chn_characpremium.etc")
            .Single(s => s.Name == name);
        if (section.Values.Count != valueCount || section.Values.Any(v => v.Kind != PvfTokenKind.Int32)
            || section.Values[1].Value is < 1 or > 255)
            throw new InvalidDataException("Invalid free stamina daily limit.");
        return section.Values[1].Value;
    }
    public double ReadGrowthExperienceBonusRate()
    {
        var type = 0;
        foreach (var section in PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/premiumlist_new.etc"))
        {
            if (section.Name == "[type]" && section.Values.Count == 1)
                type = section.Values[0].Value;
            if (type != 84 || section.Name != "[bonus exp]") continue;
            if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32
                || section.Values[0].Value < 0)
                throw new InvalidDataException("Invalid growth contract experience rate.");
            return section.Values[0].Value / 100d;
        }
        throw new InvalidDataException("Missing growth contract experience rate.");
    }

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
