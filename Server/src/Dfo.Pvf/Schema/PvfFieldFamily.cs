using System.Globalization;

namespace Dfo.Pvf.Schema;

public sealed class PvfFieldFamily(string prefix, string suffix = "]")
{
    public string Prefix { get; } = prefix;
    public string Suffix { get; } = suffix;

    public bool TryGetIndex(string name, out int index)
    {
        index = 0;
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(Suffix, StringComparison.Ordinal)
            && name.Length >= Prefix.Length + Suffix.Length
            && int.TryParse(name.AsSpan(Prefix.Length, name.Length - Prefix.Length - Suffix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
