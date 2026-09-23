namespace Dfo.Server.Data.Characters;

public readonly record struct CharacterSkillPointBudget(long Sp, long Tp);

public sealed class CharacterSkillPointLedger
{
    private readonly IReadOnlyDictionary<int, CharacterSkillPointBudget> balances;
    public IReadOnlyList<int> MissingSkillIds { get; }
    public bool IsComplete => MissingSkillIds.Count == 0;
    public CharacterSkillPointLedger(IReadOnlyDictionary<int, CharacterSkillPointBudget> balances, IEnumerable<int> missingSkillIds)
    {
        this.balances = new System.Collections.ObjectModel.ReadOnlyDictionary<int, CharacterSkillPointBudget>(new Dictionary<int, CharacterSkillPointBudget>(balances));
        MissingSkillIds = Array.AsReadOnly(missingSkillIds.Distinct().Order().ToArray());
    }
    public CharacterSkillPointLedger(long page0Sp, long page1Sp, long page0Tp, long page1Tp, IEnumerable<int> missingSkillIds)
        : this(new Dictionary<int, CharacterSkillPointBudget> { [0] = new(page0Sp,page0Tp), [1] = new(page1Sp,page1Tp) }, missingSkillIds) { }
    public CharacterSkillPointBudget For(SkillBuildId build) => balances.TryGetValue(build.Value, out var balance)
        ? balance : throw new KeyNotFoundException($"Unknown skill build {build.Value}.");
    // A21's fixed two-page projection is separate from the N-build ledger.
    public CharacterSkillProtocolMirrorState ToProtocolMirror() => new(
        Clamp(For(new(0)).Sp), Clamp(For(new(1)).Sp), Clamp(For(new(0)).Tp), Clamp(For(new(1)).Tp));
    private static ushort Clamp(long value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}
