using Dfo.Pvf;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the archive's list files, which map an identifier to a relative file name.
/// </summary>
/// <remarks>
/// A list file is a flat token stream that alternates an integer with a string; there is
/// no enclosing section. Every catalog keyed by identifier starts here.
/// </remarks>
internal static class PvfListFile
{
    public static List<(int Id, string RelativePath)> Read(PvfArchive archive, string listPath)
    {
        var listIndex = archive.FindFile(listPath);
        if (listIndex < 0)
            throw new InvalidOperationException($"The archive does not contain {listPath}.");

        var entries = new List<(int, string)>();
        var reader = archive.OpenScript(listIndex);
        var pendingId = int.MinValue;

        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                pendingId = token.Value;
            }
            else if (token.IsString && pendingId != int.MinValue)
            {
                var name = archive.ResolveString(token.Value).Replace('\\', '/');
                if (name.Length > 0)
                    entries.Add((pendingId, name));
                pendingId = int.MinValue;
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException($"{listPath} declared no entries.");

        return entries;
    }
}
