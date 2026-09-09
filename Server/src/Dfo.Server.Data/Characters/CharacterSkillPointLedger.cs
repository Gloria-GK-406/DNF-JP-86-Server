namespace Dfo.Server.Data.Characters;

public readonly record struct CharacterSkillPointBudget(long Sp, long Tp);

public sealed class CharacterSkillPointLedger(
    long page0Sp, long page1Sp, long page0Tp, long page1Tp, IEnumerable<int> missingSkillIds)
{
    public long Page0Sp { get; } = page0Sp;
    public long Page1Sp { get; } = page1Sp;
    public long Page0Tp { get; } = page0Tp;
    public long Page1Tp { get; } = page1Tp;
    public IReadOnlyList<int> MissingSkillIds { get; } = Array.AsReadOnly(missingSkillIds.Distinct().Order().ToArray());
    public bool IsComplete => MissingSkillIds.Count == 0;

    public CharacterSkillProtocolMirrorState ToProtocolMirror() => new(
        Clamp(Page0Sp), Clamp(Page1Sp), Clamp(Page0Tp), Clamp(Page1Tp));

    private static ushort Clamp(long value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}
