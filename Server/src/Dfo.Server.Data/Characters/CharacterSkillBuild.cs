namespace Dfo.Server.Data.Characters;

public readonly record struct SkillBuildId
{
    public int Value { get; }
    public SkillBuildId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }
}

public sealed record CharacterSkillBuild(SkillBuildId Id, bool IsUnlocked);
public sealed record CharacterSkillBuilds(SkillBuildId Active, IReadOnlyList<CharacterSkillBuild> Builds)
{
    public bool IsUnlocked(SkillBuildId id) => Builds.Any(build => build.Id == id && build.IsUnlocked);
}
