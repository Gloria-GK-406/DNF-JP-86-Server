using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Tests;

/// <summary>
/// Locates the operator-supplied archive. It is excluded from version control, so these
/// tests report themselves as skipped rather than failing on a machine without it.
/// </summary>
internal static class ArchiveFixture
{
    private const string OverrideVariable = "PVF_ARCHIVE_PATH";

    public static string? Path { get; } = Locate();

    public static bool Available => Path is not null;

    public const string SkipReason =
        "Script.pvf not found. Set PVF_ARCHIVE_PATH, or place the archive in the repository root or Data/Pvf/.";

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(directory.FullName, "Script.pvf"),
                         System.IO.Path.Combine(directory.FullName, "Data", "Pvf", "Script.pvf"),
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class ArchiveTests
{
    [SkippableFact]
    public void Opens_and_indexes_the_shipping_archive()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);

        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        Assert.True(archive.FileCount > 0);
        Assert.True(archive.ChunkCount > 0);
    }

    [SkippableFact]
    public void Finds_a_known_list_file_regardless_of_path_spelling()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        var canonical = archive.FindFile("stackable/stackable.lst");
        Skip.If(canonical < 0, "This archive does not contain stackable/stackable.lst.");

        Assert.Equal(canonical, archive.FindFile("STACKABLE/STACKABLE.LST"));
        Assert.Equal(canonical, archive.FindFile(@"stackable\stackable.lst"));
        Assert.Equal(canonical, archive.FindFile("./stackable/stackable.lst"));
    }

    [SkippableFact]
    public void Reports_an_absent_path_rather_than_guessing()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        Assert.Equal(-1, archive.FindFile("no/such/file.etc"));
        Assert.Equal(-1, archive.FindFile(string.Empty));
    }

    [SkippableFact]
    public void Script_files_read_back_as_whole_records()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        var index = archive.FindFile("stackable/stackable.lst");
        Skip.If(index < 0, "This archive does not contain stackable/stackable.lst.");

        var entry = archive.GetEntry(index);
        Assert.True(entry.IsScript);
        Assert.Equal(0, entry.DataSize % PvfToken.Size);

        var reader = archive.OpenScript(index);
        var records = 0;
        while (reader.Read(out _))
            records++;

        Assert.Equal(entry.DataSize / PvfToken.Size, records);
    }

    [SkippableFact]
    public void A_list_file_pairs_identifiers_with_integers()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        var index = archive.FindFile("stackable/stackable.lst");
        Skip.If(index < 0, "This archive does not contain stackable/stackable.lst.");

        var reader = archive.OpenScript(index);
        var pairs = 0;
        var pendingId = int.MinValue;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                pendingId = token.Value;
            }
            else if (token.IsString && pendingId != int.MinValue)
            {
                Assert.False(string.IsNullOrEmpty(archive.ResolveString(token.Value)));
                pendingId = int.MinValue;
                pairs++;
            }
        }

        Assert.True(pairs > 100, $"expected a populated list, found {pairs} entries");
    }

    [SkippableFact]
    public void Tags_resolve_to_one_interned_offset_and_match_by_value()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        var tag = archive.GetTag("[name]");
        Skip.IfNot(tag.IsResolved, "This archive does not use a [name] tag.");

        Assert.True(tag.Offset >= 0);
        Assert.Equal("[name]", archive.ResolveString(tag.Offset));
        Assert.True(tag.Matches(new PvfToken(PvfTokenKind.Identifier, tag.Offset)));
    }

    [SkippableFact]
    public void An_unused_tag_resolves_to_unresolved_instead_of_throwing()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);
        using var archive = PvfArchive.Open(ArchiveFixture.Path!);

        Assert.False(archive.GetTag("[no such tag exists in this archive]").IsResolved);
    }

    [SkippableFact]
    public void Cache_stays_within_its_budget_while_sweeping_the_whole_body()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);

        const long budget = 8L * 1024 * 1024;
        using var archive = PvfArchive.Open(
            ArchiveFixture.Path!,
            new PvfArchiveOptions { ChunkCacheBytes = budget });

        // Touch far more content than the budget can hold.
        var touched = 0;
        for (var i = 0; i < archive.FileCount && touched < 40_000; i += 13)
        {
            if (archive.GetEntry(i).DataSize > 0)
            {
                _ = archive.GetFileBytes(i);
                touched++;
            }
        }

        Assert.True(archive.Metrics.ChunkEvictions > 0, "the sweep should have forced eviction");
        Assert.True(
            archive.Metrics.ResidentBytes <= budget,
            $"resident {archive.Metrics.ResidentBytes} exceeded budget {budget}");
    }

    [SkippableFact]
    public void Bytes_stay_readable_after_their_chunk_is_evicted()
    {
        Skip.IfNot(ArchiveFixture.Available, ArchiveFixture.SkipReason);

        // A budget of one byte forces eviction on every admission.
        using var archive = PvfArchive.Open(
            ArchiveFixture.Path!,
            new PvfArchiveOptions { ChunkCacheBytes = 1 });

        var index = archive.FindFile("stackable/stackable.lst");
        Skip.If(index < 0, "This archive does not contain stackable/stackable.lst.");

        var held = archive.GetFileBytes(index).ToArray();
        var snapshot = archive.GetFileBytes(index);

        // Churn the cache so the chunk backing `snapshot` is evicted.
        for (var i = 0; i < archive.FileCount && i < 5_000; i += 7)
        {
            if (archive.GetEntry(i).DataSize > 0)
                _ = archive.GetFileBytes(i);
        }

        Assert.True(archive.Metrics.ChunkEvictions > 0);
        Assert.True(snapshot.SequenceEqual(held), "an evicted chunk changed the bytes a caller already held");
    }
}
