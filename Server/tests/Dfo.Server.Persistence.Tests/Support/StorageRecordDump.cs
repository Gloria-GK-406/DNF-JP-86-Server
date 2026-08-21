using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Dfo.Server.Persistence.Tests.Support;

/// <summary>
/// Everything a loaded record holds, written out one value per line.
/// </summary>
/// <remarks>
/// A characterization test needs to fail on any difference, not on the differences someone
/// thought to assert. Writing the whole record out and comparing the text does that, and it does
/// something a hash cannot: when it fails, the failure says which value moved.
///
/// Order is fixed by declaration order rather than by name, because for a record that is the order
/// the fields were meant to be read in, and because a collection whose contents were reordered has
/// changed even though its members have not.
///
/// One limit worth knowing: a type that had both public fields and public properties would list
/// all its fields before all its properties, because that is how the two tables are laid out, not
/// how the type was written. Nothing here is such a type -- records expose properties, tuples
/// expose fields -- so the order is the declared one; a type that mixed them would need its own
/// answer.
/// </remarks>
internal static class StorageRecordDump
{
    public static string Of(object? value)
    {
        var lines = new StringBuilder();
        Write(lines, string.Empty, value);
        return lines.ToString();
    }

    private static void Write(StringBuilder lines, string path, object? value)
    {
        switch (value)
        {
            case null:
                lines.Append(path).Append(" = null").Append('\n');
                return;
            case byte[] bytes:
                lines.Append(path).Append(" = 0x")
                    .Append(Convert.ToHexString(bytes)).Append('\n');
                return;
            case string text:
                lines.Append(path).Append(" = \"").Append(text).Append('"').Append('\n');
                return;
            case bool or char or Enum:
                lines.Append(path).Append(" = ").Append(value).Append('\n');
                return;
            case IFormattable formattable:
                lines.Append(path).Append(" = ")
                    .Append(formattable.ToString(null, CultureInfo.InvariantCulture)).Append('\n');
                return;
            case IEnumerable items:
                var index = 0;
                foreach (var item in items)
                {
                    Write(lines, $"{path}[{index}]", item);
                    index++;
                }

                if (index == 0)
                {
                    lines.Append(path).Append(" = []").Append('\n');
                }

                return;
        }

        var type = value.GetType();
        var members = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => (property.Name, property.MetadataToken,
                Read: (Func<object, object?>)property.GetValue))
            // A tuple carries its parts as fields, not properties. Without these, a byte array
            // sitting in one prints as its type name and every byte in it goes uncompared.
            .Concat(type
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (field.Name, field.MetadataToken,
                    Read: (Func<object, object?>)field.GetValue)))
            .OrderBy(member => member.MetadataToken)
            .ToArray();

        if (members.Length == 0)
        {
            lines.Append(path).Append(" = ").Append(value).Append('\n');
            return;
        }

        foreach (var member in members)
        {
            var child = path.Length == 0 ? member.Name : $"{path}.{member.Name}";
            Write(lines, child, member.Read(value));
        }
    }
}
