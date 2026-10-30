using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPremiumServiceDefinitionSource(PvfArchiveProvider provider) : IPremiumServiceDefinitionSource, IQuestHelperPuppetDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> FreeStaminaField = PvfField.Int32List("[free_stamina]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PlusGoldcardField = PvfField.Int32List("[plus_goldcard]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> BuffServiceField = PvfField.Int32List("[buff_service]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> AutoRepairField = PvfField.Int32List("[auto_repair]", optional: true);
        internal static readonly PvfField<int> BonusExpField = PvfField.Int32("[bonus exp]", optional: true);
        internal static readonly PvfField<int> ExpField = PvfField.Int32("[exp]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> QuestHelperField = PvfField.Int32List("[quest_helper]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> LotteryOnemoreField = PvfField.Int32List("[lottery_onemore]", optional: true);
        internal static readonly PvfField<int> TypeField = PvfField.Int32("[type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> IndependentDropRateField = PvfField.Int32List("[independent drop rate]", optional: true);
        internal static readonly PvfField<int> OverSkillField = PvfField.Int32("[over skill]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> OverEquipinfoField = PvfField.Int32List("[over equipinfo]", optional: true);
    }

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
        var section = (PvfDocument.TryOpen(provider.Archive, "etc/chn_characpremium.etc", PvfPreamble.Ignore)?.Sections ?? [])
            .Single(s => s.Name == Fields.QuestHelperField.Name);
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
        var section = (PvfDocument.TryOpen(provider.Archive, "etc/chn_characpremium.etc", PvfPreamble.Ignore)?.Sections ?? [])
            .Single(s => s.Name == Fields.LotteryOnemoreField.Name);
        if (section.Values.Count < 3 || section.Values.Any(v => v.Kind != PvfTokenKind.Int32)
            || section.Values[1].Value is < 1 or > 255 || section.Values.Skip(2).Any(v => v.Value <= 0))
            throw new InvalidDataException("Invalid double pot rules.");
        return new(section.Values[1].Value, section.Values.Skip(2).Select(v => v.Value).ToHashSet());
    }
    public int ReadFreeWeaknessDailyLimit() => ReadDailyLimit(Fields.FreeStaminaField);
    public int ReadFreeGoldCardDailyLimit() => ReadDailyLimit(Fields.PlusGoldcardField);
    public int ReadDungeonBuffDailyLimit() => ReadDailyLimit(Fields.BuffServiceField, 23);
    public int ReadAutoRepairDailyLimit() => ReadDailyLimit(Fields.AutoRepairField);
    private int ReadDailyLimit(PvfField<IReadOnlyList<int>> name, int valueCount = 2)
    {
        var section = (PvfDocument.TryOpen(provider.Archive, "etc/chn_characpremium.etc", PvfPreamble.Ignore)?.Sections ?? [])
            .Single(s => s.Name == name.Name);
        if (section.Values.Count != valueCount || section.Values.Any(v => v.Kind != PvfTokenKind.Int32)
            || section.Values[1].Value is < 1 or > 255)
            throw new InvalidDataException("Invalid free stamina daily limit.");
        return name.Decode(provider.Archive, section.Values, "etc/chn_characpremium.etc")[1];
    }
    public double ReadGrowthExperienceBonusRate() => ReadExperienceRate(84, Fields.BonusExpField);
    public double ReadBlackDiamondExperienceBonusRate() => ReadExperienceRate(56, Fields.ExpField);

    private double ReadExperienceRate(int premiumType, PvfField<int> tag)
    {
        var type = 0;
        foreach (var section in PremiumSections())
        {
            if (section.Name == Fields.TypeField.Name)
                type = Fields.TypeField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
            if (type != premiumType || section.Name != tag.Name) continue;
            if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32
                || section.Values[0].Value < 0)
                throw new InvalidDataException($"Invalid premium {premiumType} experience rate.");
            return tag.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc") / 100d;
        }
        throw new InvalidDataException($"Missing premium {premiumType} experience rate.");
    }

    public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents()
    {
        var type = 0;
        foreach (var section in PremiumSections())
        {
            if (section.Name == Fields.TypeField.Name)
                type = Fields.TypeField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
            if (type != 84 || section.Name != Fields.IndependentDropRateField.Name) continue;
            if (section.Values.Count != 4 || section.Values.Any(v => v.Kind != PvfTokenKind.Int32 || v.Value < 0))
                throw new InvalidDataException("Invalid growth contract boss equipment rates.");
            return Fields.IndependentDropRateField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
        }
        throw new InvalidDataException("Missing growth contract boss equipment rates.");
    }

    private IReadOnlyList<PvfSection> PremiumSections()
    {
        var sections = (PvfDocument.TryOpen(provider.Archive, "etc/premiumlist_new.etc", PvfPreamble.Ignore)?.Sections ?? []);
        foreach (var section in sections.Where(section => Fields.TypeField.Matches(section.Name)))
            Fields.TypeField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
        return sections;
    }

    public PremiumContractLevels ReadContractLevels()
    {
        var type = 0; var skill = 0; var equipment = 0;
        foreach (var section in PremiumSections())
        {
            if (section.Name == Fields.TypeField.Name)
                type = Fields.TypeField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
            if (type == 27 && section.Name == Fields.OverSkillField.Name)
                skill = Fields.OverSkillField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc");
            if (type == 22 && section.Name == Fields.OverEquipinfoField.Name)
            {
                var values = Fields.OverEquipinfoField.Decode(provider.Archive, section.Values, "etc/premiumlist_new.etc").ToArray();
                if (values.Length == 0) throw new InvalidDataException("Malformed over-equipment contract declaration.");
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
