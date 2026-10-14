using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal static class PvfItemLifetimeReader
{
    private static readonly PvfField<IReadOnlyList<PvfToken>> Expiration = PvfField.Tokens("[expiration date]", optional: true);
    private static readonly PvfField<int> Period = PvfField.Int32("[usable period]", optional: true);
    private static readonly PvfField<int> Daily = PvfField.Int32("[daily delete item]", optional: true);

    internal static ItemLifetimeDefinition Read(PvfArchive archive, string path)
    {
        var document = PvfDocument.Open(archive, path);
        var absolute = 0;
        if (document.TryRead(Expiration, out var values))
        {
            if (values.Count == 1 && values[0].Kind == PvfTokenKind.Int32 && values[0].Value == 0) { }
            else if (values.Count == 1 && values[0].IsString && DateTime.TryParseExact(archive.ResolveString(values[0].Value),
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var seconds = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8)).ToUnixTimeSeconds();
                if (seconds is <= 0 or >= int.MaxValue) throw new InvalidDataException($"PVF '{path}': expiration outside supported epoch.");
                absolute = (int)seconds;
            }
            else throw new InvalidDataException($"PVF '{path}': malformed [expiration date].");
        }
        var days = document.TryRead(Period, out var period) ? period : 0;
        var daily = document.TryRead(Daily, out var value) ? value : 0;
        if (days < 0 || daily is not (0 or 1)) throw new InvalidDataException($"PVF '{path}': invalid lifetime policy.");
        return new(absolute, days, daily == 1);
    }
}
