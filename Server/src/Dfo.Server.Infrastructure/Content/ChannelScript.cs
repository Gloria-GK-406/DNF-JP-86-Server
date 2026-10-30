using System.Globalization;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>One line of a <c>[server]</c> block: the block's group number and the line's fields.</summary>
/// <param name="ServerGroup">The bare number that opens the block, or -1 for a line read before it.</param>
public sealed record ChannelScriptServerRow(int ServerGroup, IReadOnlyList<string> Tokens);

/// <summary><c>channel_info.etc</c> as sections of tokenized lines, before any consumer reads meaning into them.</summary>
public sealed record ChannelScriptDocument(
    IReadOnlyList<ChannelScriptServerRow> ServerRows,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> DungeonBlocks);

/// <summary>A channel the script declares for the game listeners, with its login environment and capacity.</summary>
public sealed record ChannelScriptListener(int Id, byte LoginEnvironment, int MaximumUsers);

/// <summary>The one reader of the channel script. Every consumer goes through <see cref="Parse"/>.</summary>
public static class ChannelScript
{
    /// <summary>The archive quotes a string with a backtick at each end.</summary>
    public const char Quote = '`';

    public static ChannelScriptDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<ChannelScriptServerRow>();
        var blocks = new List<IReadOnlyList<IReadOnlyList<string>>>();
        List<IReadOnlyList<string>>? block = null;
        var inServer = false;
        var group = -1;
        foreach (var line in text.Split('\n'))
        {
            var tokens = Tokenize(line);
            if (tokens.Count == 0)
                continue;
            switch (tokens[0])
            {
                case "[dungeon]": block = []; continue;
                case "[/dungeon]": if (block is not null) blocks.Add(block); block = null; continue;
                case "[server]": inServer = true; group = -1; continue;
                case "[/server]": inServer = false; continue;
            }
            if (block is not null)
                block.Add(tokens);
            else if (inServer)
            {
                // The block opens with a bare number naming its server group; it is not a channel row.
                if (group == -1 && tokens.Count == 1
                    && int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    group = parsed;
                    continue;
                }
                rows.Add(new(group, tokens));
            }
        }
        return new(rows, blocks);
    }

    /// <summary>The channels of server group 1 the game listeners serve.</summary>
    /// <remarks>
    /// A row needs an id, a quoted display name, a type and a quoted category. PvP types 8/13/24 are
    /// served only when asked for; a <c>[raid]</c> row is the raid channel only with type 23. A
    /// repeated id keeps its first row. The capacities are the ones this server has always
    /// announced for script channels; no archive field declares them.
    /// </remarks>
    public static IReadOnlyList<ChannelScriptListener> Listeners(ChannelScriptDocument script, bool includePvp)
    {
        ArgumentNullException.ThrowIfNull(script);
        var result = new List<ChannelScriptListener>();
        var ids = new HashSet<int>();
        foreach (var row in script.ServerRows.Where(row => row.ServerGroup == 1))
        {
            var t = row.Tokens;
            if (t.Count < 4 || !Digits(t[0], out var id) || !IsQuoted(t[1]) || !Digits(t[2], out var type) || !IsQuoted(t[3]))
                continue;
            var category = Unquote(t[3]);
            if (id is < 1 or > 255 || type is < 0 or > 255 || (category == "[raid]" && type != 23)) continue;
            var pvp = type is 8 or 13 or 24;
            if (pvp && !includePvp) continue;
            if (ids.Add(id))
                result.Add(new(id, (byte)(pvp || type == 23 ? type : 1), id is 20 or 21 ? 150 : id == 200 ? 250 : 100));
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Channel script has no supported channels in server group 1.");
        return result;
    }

    public static string Unquote(string token) => token.Trim().Trim(Quote).Trim();

    private static bool IsQuoted(string token) => token.Length >= 2 && token[0] == Quote && token[^1] == Quote;

    private static bool Digits(string token, out int value)
    {
        value = 0;
        return token.Length > 0 && token.All(char.IsAsciiDigit)
            && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The line's fields, with quoted runs kept whole.</summary>
    /// <remarks>
    /// Quoting matters because a display name is quoted and a group name is quoted, while the
    /// numbers around them are not; splitting on whitespace alone works on today's file and breaks
    /// on the first quoted value that contains a space.
    /// </remarks>
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var at = 0;
        while (at < line.Length)
        {
            if (char.IsWhiteSpace(line[at]))
            {
                at++;
                continue;
            }

            var start = at;
            if (line[at] == Quote)
            {
                at++;
                while (at < line.Length && line[at] != Quote)
                    at++;
                if (at < line.Length)
                    at++;
            }
            else
            {
                while (at < line.Length && !char.IsWhiteSpace(line[at]))
                    at++;
            }

            tokens.Add(line[start..at]);
        }

        return tokens;
    }
}
