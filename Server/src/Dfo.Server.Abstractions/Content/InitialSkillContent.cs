namespace Dfo.Server.Abstractions.Content;

/// <summary>One skill a job starts with, and the level it starts at.</summary>
public readonly record struct InitialSkillGrant(uint SkillId, byte Level);

/// <summary>The skills a job's characters are created holding.</summary>
public interface IInitialSkillDefinitionSource
{
    /// <summary>
    /// The grants for this job in the order the archive declares them, or an empty list when the
    /// archive declares no such job.
    /// </summary>
    /// <remarks>
    /// Order is preserved rather than sorted. It is the archive's own, and the client's quick-slot
    /// defaults are the one thing that might read meaning into it; sorting here would destroy that
    /// before we have looked.
    /// </remarks>
    ValueTask<IReadOnlyList<InitialSkillGrant>> GetAsync(byte job, CancellationToken cancellationToken);
}
