using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfIndependentDropTableSourceTests
{
    private static IndependentDropToken N(int value) => new(value, null);
    private static IndependentDropToken T(string tag) => new(null, tag);
    private static int[] Fields(int mode = 0) => [0, 17, 3229, 300000, 400000, 500000, 600000, 600000,
        1, 3, 5, 8, 4, 0, 0, -1, mode];

    [Fact]
    public void Parser_retains_raw_fields_row_order_and_duplicate_pool_choices()
    {
        var fields = Fields(1);
        fields[0] = 1;
        fields[2] = 0;
        fields[3] = 3500000;
        var tokens = new[] { T("[independent drop]") }.Concat(fields.Select(N)).Concat(new[] {
            T("[list]"), N(3030), N(100), N(3030), N(300), T("[/list]"), T("[/independent drop]") }).ToArray();
        var row = Assert.Single(PvfIndependentDropTableSource.Parse(tokens));
        Assert.Equal(fields, row.Fields.ToArray());
        Assert.Equal(new[] { 100, 300 }, row.Choices.Select(choice => choice.Weight));
        Assert.Equal(0, row.Ordinal);
    }

    [Fact]
    public void Missing_fields_or_list_terminators_fail_with_source_context()
    {
        var truncated = new[] { T("[independent drop]") }.Concat(Fields().Take(16).Select(N)).ToArray();
        Assert.Contains("independent_drop.etc", Assert.Throws<InvalidDataException>(
            () => PvfIndependentDropTableSource.Parse(truncated)).Message);
        var oddList = new[] { T("[independent drop]") }.Concat(Fields(1).Select(N)).Concat(new[] {
            T("[list]"), N(3030), T("[/list]"), T("[/independent drop]") }).ToArray();
        Assert.Throws<InvalidDataException>(() => PvfIndependentDropTableSource.Parse(oddList));
    }

    [Theory]
    [InlineData(0, 2)] [InlineData(3, -1)] [InlineData(8, -1)] [InlineData(16, 2)]
    public void Unknown_source_mode_and_negative_probabilities_or_counts_fail(int field, int value)
    {
        var fields = Fields();
        fields[field] = value;
        var tokens = new[] { T("[independent drop]") }.Concat(fields.Select(N))
            .Append(T("[/independent drop]")).ToArray();
        Assert.Throws<InvalidDataException>(() => PvfIndependentDropTableSource.Parse(tokens));
    }

    [SkippableFact]
    public void A21_complete_table_is_retained_including_unresolved_types()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var table = new PvfIndependentDropTableSource(provider).Load();
        Assert.Equal(3046, table.Rows.Length);
        Assert.Equal(374, table.Rows.Count(row => row.SourceType == 1));
        Assert.Equal(4953, table.Rows.Sum(row => row.Choices.Length));
        Assert.Equal(1961, table.Items.Count);
        Assert.Equal(10150672, Assert.Single(table.Items, item => item.Value is null).Key);
        Assert.Contains(table.Rows, row => row.SourceId == 65696 && row.ItemId == 10150672);
        Assert.Contains(table.Rows, row => row.Fields[3] == 3500000);
    }
}
