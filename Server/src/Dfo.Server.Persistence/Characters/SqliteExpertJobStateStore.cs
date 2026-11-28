using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteExpertJobStateStore(SqliteDatabase database, ICharacterExpertJobTable jobs,
    ICharacterExpertRecipeTable recipes) : IExpertJobStateStore
{
    public async ValueTask<ExpertJobState> ReadAsync(CharacterId character, CancellationToken ct)
    {
        var row = await jobs.FindAsync(character, ct).ConfigureAwait(false);
        if (row is null) return ExpertJobState.Empty;
        if (row.ExpertJobType is < 0 or > (int)ExpertJobType.DollController || row.GiveUpCount is < 0 or > byte.MaxValue)
            throw new InvalidDataException("Invalid stored expert-job identity.");
        if (row.ExpertJobType == 0)
            return ExpertJobState.Empty with { GiveUpCount = (byte)row.GiveUpCount };
        if (row.ExpertJobExperience is < 0 or > uint.MaxValue)
            throw new InvalidDataException("Invalid stored expert-job experience.");
        var state = new ExpertJobState((ExpertJobType)row.ExpertJobType, (uint)row.ExpertJobExperience,
            (byte)row.GiveUpCount, row.DisjointMachineGrade, row.DisjointMachineEndurance,
            row.EnchanterEndurance, await recipes.ReadAsync(character, ct).ConfigureAwait(false));
        Validate(state);
        return state;
    }

    public async ValueTask WriteAsync(CharacterId character, ExpertJobState state, CancellationToken ct)
    {
        Validate(state);
        await database.ExecuteAsync("""
            INSERT INTO character_expert_job (character_id, expert_job_type, expert_job_exp, giveup_count,
                disjoint_machine_grade, disjoint_machine_endurance, enchanter_endurance)
            VALUES (@id, @type, @exp, @count, @grade, @disjoint, @enchant)
            ON CONFLICT(character_id) DO UPDATE SET expert_job_type = excluded.expert_job_type,
                expert_job_exp = excluded.expert_job_exp, giveup_count = excluded.giveup_count,
                disjoint_machine_grade = excluded.disjoint_machine_grade,
                disjoint_machine_endurance = excluded.disjoint_machine_endurance,
                enchanter_endurance = excluded.enchanter_endurance;
            """, p =>
        {
            p.AddWithValue("@id", character.Value);
            p.AddWithValue("@type", (byte)state.Type);
            p.AddWithValue("@exp", (long)state.Experience);
            p.AddWithValue("@count", state.GiveUpCount);
            p.AddWithValue("@grade", state.DisjointMachineGrade);
            p.AddWithValue("@disjoint", state.DisjointMachineEndurance);
            p.AddWithValue("@enchant", state.EnchanterEndurance);
        }, ct).ConfigureAwait(false);

        var before = await recipes.ReadAsync(character, ct).ConfigureAwait(false);
        foreach (var removed in before.Except(state.LearnedRecipeIds))
            await database.ExecuteAsync("DELETE FROM character_expert_job_recipes WHERE character_id = @id AND recipe_id = @recipe;",
                p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@recipe", removed); }, ct).ConfigureAwait(false);
        foreach (var added in state.LearnedRecipeIds.Except(before))
            await database.ExecuteAsync("INSERT INTO character_expert_job_recipes (character_id, recipe_id) VALUES (@id, @recipe);",
                p => { p.AddWithValue("@id", character.Value); p.AddWithValue("@recipe", added); }, ct).ConfigureAwait(false);
    }

    private static void Validate(ExpertJobState state)
    {
        if (!Enum.IsDefined(state.Type) || state.DisjointMachineGrade < 0 || state.DisjointMachineEndurance < 0
            || state.EnchanterEndurance < 0 || state.LearnedRecipeIds.Count > byte.MaxValue
            || state.LearnedRecipeIds.Any(id => id <= 0))
            throw new InvalidDataException("Invalid expert-job state.");
    }

    public ValueTask InitializeAsync(CharacterId character, CancellationToken ct) =>
        jobs.InsertIfAbsentAsync(character, ct);

    public async ValueTask DeleteAsync(CharacterId character, CancellationToken ct)
    {
        await database.ExecuteAsync("DELETE FROM character_expert_job_recipes WHERE character_id = @id;",
            p => p.AddWithValue("@id", character.Value), ct).ConfigureAwait(false);
        await database.ExecuteAsync("DELETE FROM character_expert_job WHERE character_id = @id;",
            p => p.AddWithValue("@id", character.Value), ct).ConfigureAwait(false);
    }
}
