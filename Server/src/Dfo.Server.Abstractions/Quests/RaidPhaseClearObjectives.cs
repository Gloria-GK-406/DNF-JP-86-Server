namespace Dfo.Server.Abstractions.Quests;

public readonly record struct RaidPhaseClearObjective(int Channel, int Phase, int Required, int Role)
{
    public bool Matches(byte phase, byte role) => Phase == phase && (Role == -1 || Role == role);
}

public static class RaidPhaseClearObjectives
{
    public const string Type = "raid phase clear";

    public static IReadOnlyList<RaidPhaseClearObjective> Read(IReadOnlyList<int> values)
    {
        if (values.Count is not (3 or 6))
            throw new InvalidDataException("An Anton raid clear objective requires one or two phase/count/role rows.");

        var goals = new RaidPhaseClearObjective[values.Count / 3];
        for (var i = 0; i < goals.Length; i++)
        {
            var phase = values[3 * i];
            var count = values[3 * i + 1];
            var role = values[3 * i + 2];
            if (phase is not (0 or 1) || count is < 1 or > 511 || role is < -1 or > 2
                || i > 0 && goals[0].Phase == phase)
                throw new InvalidDataException("Invalid Anton raid clear objective.");

            goals[i] = new RaidPhaseClearObjective(i, phase, count, role);
        }
        return goals;
    }
}
