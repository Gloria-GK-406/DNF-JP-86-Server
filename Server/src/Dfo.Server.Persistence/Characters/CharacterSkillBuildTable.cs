using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteCharacterSkillBuildTable(SqliteDatabase database) : ICharacterSkillBuildTable
{
    public async ValueTask<CharacterSkillBuilds> ReadAsync(CharacterId id, CancellationToken ct)
    {
        var active = await database.ReadAllAsync(
            "SELECT COALESCE(active_skill_build,0) FROM characters WHERE character_id=@cid",
            p => p.AddWithValue("@cid", id.Value), r => r.GetInt32(0), ct);
        if (active.Count != 1) throw new InvalidOperationException("Skill build character is unavailable.");
        var rows = await database.ReadAllAsync(
            "SELECT build_id,unlocked FROM character_skill_builds WHERE character_id=@cid ORDER BY build_id",
            p => p.AddWithValue("@cid", id.Value), r => new CharacterSkillBuild(new(r.GetInt32(0)), r.GetInt32(1) != 0), ct);
        if (rows.Count != 0) return new(new(active[0]), rows);
        // A character whose pages were never changed is projected without writes: page 0, a locked
        // page 1 and any page holding skills. Its first change materializes this state.
        var ids = await database.ReadAllAsync(
            "SELECT DISTINCT page_index FROM character_skills WHERE character_id=@cid AND mode='pve'",
            p => p.AddWithValue("@cid", id.Value), r => r.GetInt32(0), ct);
        return new(new(active[0]), ids.Append(0).Append(1).Distinct().Order()
            .Select(i => new CharacterSkillBuild(new(i), i != 1)).ToArray());
    }

    public async ValueTask EnsureAsync(CharacterId id, CancellationToken ct)
    {
        var state = await ReadAsync(id, ct);
        foreach (var build in state.Builds)
            await database.ExecuteAsync("INSERT OR IGNORE INTO character_skill_builds(character_id,build_id,unlocked) VALUES(@cid,@build,@unlocked)",
                p => { p.AddWithValue("@cid", id.Value); p.AddWithValue("@build", build.Id.Value); p.AddWithValue("@unlocked", build.IsUnlocked ? 1 : 0); }, ct);
        await database.ExecuteAsync("UPDATE characters SET active_skill_build=COALESCE(active_skill_build,@build) WHERE character_id=@cid",
            p => { p.AddWithValue("@cid", id.Value); p.AddWithValue("@build", state.Active.Value); }, ct);
    }

    public async ValueTask AddAsync(CharacterId id, SkillBuildId build, CancellationToken ct)
    {
        await EnsureAsync(id, ct);
        await database.ExecuteAsync("INSERT INTO character_skill_builds(character_id,build_id,unlocked) VALUES(@cid,@build,1) ON CONFLICT(character_id,build_id) DO UPDATE SET unlocked=1",
            p => { p.AddWithValue("@cid", id.Value); p.AddWithValue("@build", build.Value); }, ct);
    }

    public async ValueTask SetActiveAsync(CharacterId id, SkillBuildId build, CancellationToken ct)
    {
        await EnsureAsync(id, ct);
        var changed = await database.ExecuteAsync("UPDATE characters SET active_skill_build=@build WHERE character_id=@cid AND EXISTS(SELECT 1 FROM character_skill_builds WHERE character_id=@cid AND build_id=@build AND unlocked=1)",
            p => { p.AddWithValue("@cid", id.Value); p.AddWithValue("@build", build.Value); }, ct);
        if (changed != 1) throw new InvalidOperationException("Skill build is locked or unavailable.");
    }
}
