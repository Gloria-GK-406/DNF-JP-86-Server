using Dfo.Server.Infrastructure.Content;
using Xunit;

namespace Dfo.Server.Testing;

// Content integration tests must run when an archive is available, and report a visible
// missing prerequisite on clean CI machines. An invalid supplied archive still fails.
public sealed class PvfFactAttribute : FactAttribute
{
    public PvfFactAttribute()
    {
        if (new PvfContentOptions().ResolveArchivePath() is null)
            Skip = "Script.pvf unavailable; set PVF_ARCHIVE_PATH to run content integration tests.";
    }
}

public sealed class PvfTheoryAttribute : TheoryAttribute
{
    public PvfTheoryAttribute()
    {
        if (new PvfContentOptions().ResolveArchivePath() is null)
            Skip = "Script.pvf unavailable; set PVF_ARCHIVE_PATH to run content integration tests.";
    }
}
