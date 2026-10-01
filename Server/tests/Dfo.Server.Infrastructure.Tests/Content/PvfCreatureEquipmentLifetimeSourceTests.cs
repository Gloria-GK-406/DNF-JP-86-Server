using Dfo.Pvf.Schema;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tokens;
using System.Globalization;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCreatureEquipmentLifetimeSourceTests
{
    private static readonly DateTimeOffset PreparedAt =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Theory]
    [InlineData("900", 900u, null)]
    [InlineData("100000000", 100_000_000u, null)]
    [InlineData("1700000900", null, 1_700_000_900L)]
    [InlineData("2015-01-02 03:04:05", null, 1_420_139_045L)]
    public void Expiration_metadata_preserves_duration_or_absolute_time(
        string raw, uint? duration, long? absolute)
    {
        Assert.True(PvfCreatureEquipmentLifetimeSource.TryParse(raw, out var result));
        Assert.Equal(duration, result.DurationSeconds);
        Assert.Equal(absolute, result.ExpiresAtUnixSeconds);
    }

    [Theory]
    [InlineData(null, "3", 0u, 259_200u)]
    [InlineData("0", "3", 0u, 259_200u)]
    [InlineData("1700000900", "3", 900u, 259_200u)]
    public void Usable_period_equipment_policy_is_preserved_beside_the_direct_probe(
        string? expiration, string usablePeriod, uint direct, uint detailFallback)
    {
        var result = PvfCreatureEquipmentLifetimeSource.ResolveMetadata(
            expiration, usablePeriod, PreparedAt);

        Assert.Equal(direct, result.DirectRemainingSeconds);
        Assert.Equal(detailFallback, result.DetailFallbackRemainingSeconds);
    }

    [SkippableFact]
    public async Task Real_archive_usable_period_only_equipment_produces_detail_fallback()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var expiration = archive.GetTag(PvfField.Tokens("[expiration date]"));
        var usablePeriod = archive.GetTag(PvfField.Tokens("[usable period]"));
        var candidate = default((int ItemId, int Days)?);

        foreach (var (itemId, relativePath) in PvfListFile.Read(archive, "equipment/equipment.lst"))
        {
            var reader = archive.OpenScript("equipment/" + relativePath);
            var section = -1;
            string? expirationValue = null;
            int? days = null;
            while (reader.Read(out var token))
            {
                if (token.Kind == PvfTokenKind.Identifier) section = token.Value;
                else if (section == usablePeriod.Offset && token.Kind == PvfTokenKind.Int32)
                    days ??= token.Value;
                else if (section == expiration.Offset)
                    expirationValue ??= token.Kind == PvfTokenKind.Int32
                        ? token.Value.ToString(CultureInfo.InvariantCulture)
                        : token.IsString ? archive.ResolveString(token.Value) : null;
            }
            if (days > 0 && (string.IsNullOrWhiteSpace(expirationValue)
                    || expirationValue.Trim().Trim('`').Trim() == "0"))
            {
                candidate = (itemId, days.Value);
                break;
            }
        }

        Assert.True(candidate.HasValue, "Script.pvf has no usable-period-only equipment definition.");
        var source = new PvfCreatureEquipmentLifetimeSource(provider);
        var result = await source.GetAsync(candidate.Value.ItemId, PreparedAt, CancellationToken.None);

        Assert.Equal(0u, result.DirectRemainingSeconds);
        Assert.Equal(checked((uint)candidate.Value.Days * 86_400u),
            result.DetailFallbackRemainingSeconds);
    }
}
