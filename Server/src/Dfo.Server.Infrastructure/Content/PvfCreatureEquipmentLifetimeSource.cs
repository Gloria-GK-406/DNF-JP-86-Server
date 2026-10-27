using Dfo.Pvf.Schema;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCreatureEquipmentLifetimeSource(PvfArchiveProvider provider)
    : ICreatureEquipmentLifetimeSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> ExpirationDateField = PvfField.Custom<string>("[expiration date]",
            (archive, values, path) =>
            {
                var value = ReadScalar(archive, values, path, "[expiration date]");
                if (value.Trim().Trim('`').Trim().Length > 0 && !TryParse(value, out _))
                    throw new InvalidDataException($"PVF '{path}', field '[expiration date]': invalid date or duration.");
                return value;
            }, optional: true);
        internal static readonly PvfField<string> UsablePeriodField = PvfField.Custom<string>("[usable period]",
            (archive, values, path) =>
            {
                var value = ReadScalar(archive, values, path, "[usable period]");
                var text = value.Trim().Trim('`').Trim();
                if (text.Length > 0 && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days < 0))
                    throw new InvalidDataException($"PVF '{path}', field '[usable period]': expected nonnegative day count.");
                return value;
            }, optional: true);
    }

    private const string EquipmentListPath = "equipment/equipment.lst";
    private readonly Lock gate = new();
    private Dictionary<int, Metadata>? metadata;

    public string Name => "creature-equipment-lifetimes";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Load().Count);
    }

    public ValueTask<CreatureEquipmentLifetime> GetAsync(
        int itemTemplateId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = Load();
        return ValueTask.FromResult(values.TryGetValue(itemTemplateId, out var value)
            ? ResolveMetadata(value.Expiration, value.UsablePeriod, preparedAt)
            : default);
    }

    private Dictionary<int, Metadata> Load()
    {
        if (metadata is not null) return metadata;
        lock (gate) return metadata ??= Build(provider.Archive);
    }

    private static Dictionary<int, Metadata> Build(PvfArchive archive)
    {
        var result = new Dictionary<int, Metadata>();
        foreach (var (itemId, relativePath) in PvfListFile.Read(archive, EquipmentListPath))
        {
            var path = "equipment/" + relativePath;
            string? expirationValue = null;
            string? usablePeriodValue = null;
            foreach (var section in PvfDocument.Open(archive, path).Sections)
            {
                if (Fields.ExpirationDateField.Matches(section.Name))
                {
                    var value = Fields.ExpirationDateField.Decode(archive, section.Values, path);
                    expirationValue ??= value;
                }
                else if (Fields.UsablePeriodField.Matches(section.Name))
                {
                    var value = Fields.UsablePeriodField.Decode(archive, section.Values, path);
                    usablePeriodValue ??= value;
                }
            }
            if (expirationValue is not null || usablePeriodValue is not null)
                result.TryAdd(itemId, new Metadata(expirationValue, usablePeriodValue));
        }
        return result;
    }

    private static string ReadScalar(PvfArchive archive, IReadOnlyList<PvfToken> values, string path, string field)
    {
        if (values.Count != 1 || (values[0].Kind != PvfTokenKind.Int32 && !values[0].IsString))
            throw new InvalidDataException($"PVF '{path}', field '{field}': expected exactly one Int32 or string.");
        return values[0].Kind == PvfTokenKind.Int32
            ? values[0].Value.ToString(CultureInfo.InvariantCulture) : archive.ResolveString(values[0].Value);
    }

    internal static CreatureEquipmentLifetime ResolveMetadata(
        string? expiration,
        string? usablePeriod,
        DateTimeOffset preparedAt)
    {
        var direct = TryParse(expiration ?? string.Empty, out var directLifetime)
            ? Remaining(directLifetime, preparedAt)
            : 0;

        var policy = TryResolvePolicy(expiration, usablePeriod, preparedAt, out var remaining)
            ? remaining
            : 0;
        return new CreatureEquipmentLifetime(direct, policy);
    }

    private static bool TryResolvePolicy(
        string? expiration,
        string? usablePeriod,
        DateTimeOffset preparedAt,
        out uint remaining)
    {
        remaining = 0;
        var usable = (usablePeriod ?? string.Empty).Trim().Trim('`').Trim();
        var days = 0;
        if (usable.Length > 0 && (!int.TryParse(usable, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out days) || days < 0))
            return false;

        var absolute = (expiration ?? string.Empty).Trim().Trim('`').Trim();
        long expiresAt = 0;
        if (absolute.Length > 0 && !TryParsePolicyExpiration(absolute, out expiresAt))
            return false;

        var preparedSeconds = preparedAt.ToUnixTimeSeconds();
        if (days > 0)
            expiresAt = Math.Min(int.MaxValue, preparedSeconds + (long)days * 86_400L);

        remaining = checked((uint)Math.Clamp(expiresAt - preparedSeconds, 0, uint.MaxValue));
        return true;
    }

    private static bool TryParsePolicyExpiration(string value, out long expiresAt)
    {
        expiresAt = 0;
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return TryToUnixSeconds(parsed, out expiresAt);

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return false;
        if (numeric == 0)
            return true;
        if (numeric >= 1_000_000_000)
        {
            expiresAt = numeric;
            return true;
        }
        return numeric > 0
            && DateTime.TryParseExact(numeric.ToString(CultureInfo.InvariantCulture), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            && TryToUnixSeconds(parsed, out expiresAt);
    }

    private static bool TryToUnixSeconds(DateTime parsed, out long seconds)
    {
        seconds = new DateTimeOffset(
            DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(8))
            .ToUnixTimeSeconds();
        return seconds is > 0 and <= int.MaxValue;
    }

    private static uint Remaining(Lifetime lifetime, DateTimeOffset preparedAt)
    {
        if (lifetime.DurationSeconds is { } duration)
            return duration;
        var remaining = lifetime.ExpiresAtUnixSeconds!.Value - preparedAt.ToUnixTimeSeconds();
        return checked((uint)Math.Clamp(remaining, 0, uint.MaxValue));
    }

    internal static bool TryParse(string raw, out Lifetime lifetime)
    {
        lifetime = default;
        var value = (raw ?? string.Empty).Trim().Trim('`').Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            if (numeric <= 0)
            {
                lifetime = new Lifetime(0, null);
                return true;
            }
            if (numeric < 1_000_000_000)
            {
                lifetime = new Lifetime(checked((uint)numeric), null);
                return true;
            }
            if (numeric >= 1_000_000_000)
            {
                lifetime = new Lifetime(null, numeric);
                return true;
            }
        }
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyyMMdd"];
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        if (!TryToUnixSeconds(parsed, out var seconds)) return false;
        lifetime = new Lifetime(null, seconds);
        return true;
    }

    internal readonly record struct Lifetime(uint? DurationSeconds, long? ExpiresAtUnixSeconds);
    private readonly record struct Metadata(string? Expiration, string? UsablePeriod);
}
