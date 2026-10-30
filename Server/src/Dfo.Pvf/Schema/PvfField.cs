using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Schema;

public abstract class PvfField
{
    protected PvfField(string name, bool optional, PvfTokenKind tokenKind)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (tokenKind is not (PvfTokenKind.Identifier or PvfTokenKind.QuotedString
            or PvfTokenKind.BlockMarker5 or PvfTokenKind.BlockMarker7))
            throw new ArgumentOutOfRangeException(nameof(tokenKind));
        Name = name;
        TokenKind = tokenKind;
        Optional = optional;
    }

    public string Name { get; }
    public bool Optional { get; }
    public PvfTokenKind TokenKind { get; }
    public bool Matches(string name) => string.Equals(Name, name, StringComparison.Ordinal);
    public bool Matches(PvfArchive archive, in PvfToken token) =>
        token.Kind == TokenKind && Matches(archive.ResolveString(token.Value));

    public TagRef Resolve(PvfArchive archive) => TokenKind == PvfTokenKind.Identifier
        ? archive.GetTag(Name)
        : throw new ArgumentException($"Field {Name} is a {TokenKind} marker, not an Identifier section.");
    public override string ToString() => Name;
    public abstract void Validate(PvfArchive archive, IReadOnlyList<PvfToken> values, string source);

    public static PvfField<T> Custom<T>(string name,
        Func<PvfArchive, IReadOnlyList<PvfToken>, string, T> decoder, bool optional = false)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return new(name, optional, decoder);
    }

    public static PvfField<int> Int32(string name, bool optional = false,
        int minimum = int.MinValue, int maximum = int.MaxValue) => new(name, optional, (archive, values, source) =>
    {
        Require(values.Count == 1 && values[0].Kind == PvfTokenKind.Int32, source, name, "expected exactly one Int32");
        var value = values[0].Value;
        Require(value >= minimum && value <= maximum, source, name, $"value {value} is outside [{minimum}, {maximum}]");
        return value;
    });

    public static PvfField<ushort?> OptionalUInt16(string name) => new(name, true, (archive, values, source) =>
    {
        var value = Int32(name, minimum: 0, maximum: ushort.MaxValue).Decode(archive, values, source);
        return (ushort)value;
    });

    public static PvfField<double> Number(string name, bool optional = false) => new(name, optional,
        (archive, values, source) =>
        {
            Require(values.Count == 1, source, name, "expected exactly one number");
            return Numeric(values[0], source, name);
        });

    public static PvfField<double> Single(string name, bool optional = false) => new(name, optional,
        (archive, values, source) =>
        {
            Require(values.Count == 1 && values[0].Kind == PvfTokenKind.Single, source, name, "expected exactly one Single");
            return Numeric(values[0], source, name);
        });

    public static PvfField<string> String(string name, bool optional = false) => new(name, optional,
        (archive, values, source) =>
        {
            Require(values.Count == 1 && values[0].IsString, source, name, "expected exactly one string");
            return archive.ResolveString(values[0].Value);
        });

    public static PvfField<IReadOnlyList<int>> Int32List(string name, bool optional = false) => new(name, optional,
        (archive, values, source) => Array.AsReadOnly(values.Select(value =>
        {
            Require(value.Kind == PvfTokenKind.Int32, source, name, "expected Int32 list");
            return value.Value;
        }).ToArray()));

    public static PvfField<IReadOnlyList<double>> Numbers(string name, bool optional = false) => new(name, optional,
        (archive, values, source) => Array.AsReadOnly(values.Select(value => Numeric(value, source, name)).ToArray()));

    public static PvfField<IReadOnlyList<string>> Strings(string name, bool optional = false) => new(name, optional,
        (archive, values, source) => Array.AsReadOnly(values.Select(value =>
        {
            Require(value.IsString, source, name, "expected string list");
            return archive.ResolveString(value.Value);
        }).ToArray()));

    // Mixed rows and nested blocks retain their specialised consumer validation.
    public static PvfField<IReadOnlyList<PvfToken>> Tokens(string name, bool optional = false,
        PvfTokenKind tokenKind = PvfTokenKind.Identifier) => new(name, optional,
        (archive, values, source) => values, tokenKind);

    public static PvfField<bool> Marker(string name, bool optional = false,
        PvfTokenKind tokenKind = PvfTokenKind.Identifier) => new(name, optional,
        (archive, values, source) =>
        {
            Require(values.Count == 0, source, name, "expected an empty block marker");
            return true;
        }, tokenKind);

    public static void Require(bool condition, string source, string name, string reason)
    {
        if (!condition) throw new InvalidDataException($"PVF '{source}', field '{name}': {reason}.");
    }

    /// <summary>Fails a whole-file check with the path and the reason, as every PVF reader reports it.</summary>
    public static void Require(bool condition, string source, string reason)
    {
        if (!condition) throw new InvalidDataException($"PVF '{source}': {reason}.");
    }

    private static double Numeric(PvfToken value, string source, string name)
    {
        var number = value.Kind switch
        {
            PvfTokenKind.Int32 => value.Value,
            PvfTokenKind.Single => (double)value.AsSingle(),
            _ => double.NaN,
        };
        Require(double.IsFinite(number), source, name, "expected a finite number");
        return number;
    }
}

public sealed class PvfField<T> : PvfField
{
    private readonly Func<PvfArchive, IReadOnlyList<PvfToken>, string, T> decoder;

    internal PvfField(string name, bool optional, Func<PvfArchive, IReadOnlyList<PvfToken>, string, T> decoder,
        PvfTokenKind tokenKind = PvfTokenKind.Identifier)
        : base(name, optional, tokenKind) => this.decoder = decoder;

    public T Decode(PvfArchive archive, IReadOnlyList<PvfToken> values, string source) => decoder(archive, values, source);
    public override void Validate(PvfArchive archive, IReadOnlyList<PvfToken> values, string source) => _ = Decode(archive, values, source);
}
