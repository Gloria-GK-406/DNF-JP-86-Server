namespace Dfo.Server.Data.ExpertJobs;

public enum ExpertJobType : byte
{
    None,
    Enchanter,
    Alchemist,
    Disjointer,
    DollController,
}

public sealed record ExpertJobState(
    ExpertJobType Type,
    uint Experience,
    byte GiveUpCount,
    int DisjointMachineGrade,
    int DisjointMachineEndurance,
    int EnchanterEndurance,
    IReadOnlyList<int> LearnedRecipeIds)
{
    public static ExpertJobState Empty { get; } = new(ExpertJobType.None, 0, 0, 0, 0, 0, []);
}
