using System.Globalization;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads which channel pays extra for which dungeons out of the channel script.
/// </summary>
/// <remarks>
/// The same file this server already sends the client verbatim, parsed for the first time. Two of
/// its sections matter here. A <c>[dungeon]</c> block names a group and lists that group's
/// dungeons; a <c>[server]</c> row binds a channel to one group and declares the percentage that
/// channel adds. The row carries further fields -- level bands, capacities -- and none of them has
/// anything to do with experience.
/// <para>
/// <b>A contradiction disqualifies rather than picks.</b> A group named twice, or a channel listed
/// twice, is the archive saying two things; answering with either one would be this server
/// choosing, and the choice would be invisible. Such a group or channel simply pays nothing.
/// </para>
/// <para>
/// <b>Nothing here can stop the server.</b> Text this cannot make sense of yields an empty
/// catalogue and a log line: no channel pays, every other bonus is untouched, and a kill still
/// awards what it is worth. The file being missing altogether is a different matter and already
/// refuses startup elsewhere -- existing behaviour, and not this reader's to change.
/// </para>
/// </remarks>
internal sealed class ChannelScriptExperienceSource(
    FileChannelScriptSource script,
    ILogger<ChannelScriptExperienceSource> logger) : IChannelExperienceSource
{
    /// <summary>The whole percentage a row may declare, at either end.</summary>
    private const int MinimumPercent = 0;
    private const int MaximumPercent = 100;

    private readonly Lock _gate = new();
    private ChannelExperienceCatalog? _catalog;

    public ChannelExperienceCatalog Load()
    {
        lock (this._gate)
        {
            return this._catalog ??= this.Read();
        }
    }

    private ChannelExperienceCatalog Read()
    {
        string text;
        try
        {
            var content = script.ReadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            // Latin1, not UTF-8. Group names are only ever compared against each other, never
            // shown, and Latin1 is the one decoding that cannot collapse two different byte
            // sequences into the same string -- which is what a replacement character does to a
            // file written in a codepage this process does not know.
            text = Encoding.Latin1.GetString(content);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "The channel script could not be read, so no channel pays an experience reward");
            return ChannelExperienceCatalog.Empty;
        }

        var catalog = Parse(text);
        logger.LogInformation(
            "Channel experience: {Channels} channel(s) declare a reward",
            catalog.Entries.Count);
        return catalog;
    }

    /// <summary>The catalogue this text declares.</summary>
    internal static ChannelExperienceCatalog Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var groups = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var contradictedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<int, ChannelRow>();
        var contradictedChannels = new HashSet<int>();

        var script = ChannelScript.Parse(text);
        foreach (var block in script.DungeonBlocks)
        {
            string? groupName = null;
            var groupDungeons = new HashSet<int>();
            foreach (var tokens in block)
                ReadDungeonLine(tokens, ref groupName, groupDungeons);
            if (groupName is not null)
                Declare(groups, contradictedGroups, groupName, groupDungeons);
        }
        foreach (var row in script.ServerRows)
            ReadServerLine(row.Tokens, rows, contradictedChannels);

        var entries = new Dictionary<int, ChannelExperienceEntry>();
        foreach (var (channelId, row) in rows)
        {
            // A row naming a group the file never described is not a contradiction -- several rows
            // name a group of no dungeons at all -- it is a channel with none of its own. It pays
            // nothing because nothing is ever in its group, which is the same answer either way.
            IReadOnlySet<int> dungeons =
                !contradictedGroups.Contains(row.Group)
                && groups.TryGetValue(row.Group, out var found)
                    ? found
                    : new HashSet<int>();
            entries[channelId] = new ChannelExperienceEntry(
                channelId, row.Group, row.Percent / 100.0, dungeons);
        }

        return new ChannelExperienceCatalog(entries);
    }

    private static void ReadDungeonLine(
        IReadOnlyList<string> tokens, ref string? groupName, HashSet<int>? dungeons)
    {
        foreach (var token in tokens)
        {
            // The group's own name is the first quoted bracketed token in the block. The other
            // quoted token is the display name the client looks up, which is not a name this
            // server can match anything against.
            var unquoted = Unquote(token);
            if (unquoted.StartsWith('[') && unquoted.EndsWith(']'))
            {
                groupName ??= unquoted;
                continue;
            }

            if (dungeons is not null && int.TryParse(
                    unquoted, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dungeonId)
                && dungeonId > 0)
            {
                dungeons.Add(dungeonId);
            }
        }
    }

    private static void ReadServerLine(
        IReadOnlyList<string> tokens,
        IDictionary<int, ChannelRow> rows,
        ISet<int> contradicted)
    {
        if (!int.TryParse(
                Unquote(tokens[0]), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var channelId)
            || channelId <= 0)
        {
            return;
        }

        // The section opens with a bare count. It is not a channel row, and reading it as one
        // would poison whichever real channel happens to share its number.
        if (tokens.Count == 1)
            return;

        if (tokens.Count < 5
            || !int.TryParse(
                Unquote(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind)
            || kind < 0
            || !int.TryParse(
                Unquote(tokens[4]), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var percent)
            || percent < MinimumPercent
            || percent > MaximumPercent)
        {
            // A row this cannot read is a declaration about this channel saying something unknown.
            // Paying nothing is the only answer that cannot be wrong in either direction.
            contradicted.Add(channelId);
            rows.Remove(channelId);
            return;
        }

        if (contradicted.Contains(channelId))
            return;

        if (!rows.TryAdd(channelId, new ChannelRow(Unquote(tokens[3]), percent)))
        {
            contradicted.Add(channelId);
            rows.Remove(channelId);
        }
    }

    private static void Declare(
        IDictionary<string, HashSet<int>> groups,
        ISet<string> contradicted,
        string name,
        HashSet<int> dungeons)
    {
        if (contradicted.Contains(name))
            return;

        if (!groups.TryAdd(name, dungeons))
        {
            contradicted.Add(name);
            groups.Remove(name);
        }
    }

    private static string Unquote(string token) => ChannelScript.Unquote(token);

    private readonly record struct ChannelRow(string Group, int Percent);
}
