using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

// The blacklist reply more than one place sends: the Blacklist slice answers CMD 0x0078 with it, and
// whatever pushes a full friend list (NOTI 0x0111 subcmd 0) has to send it straight after, because
// that list wipes the client's blacklist entries (research/client-evidence.md, RQ-004).

/// <summary>One line of the blacklist as the client shows it: who, and the day they were added.</summary>
public sealed record BlacklistListEntry(byte[] Name, DateOnly Since);

/// <summary>CMD <c>0x0078</c> reply: the requester's whole blacklist.</summary>
/// <remarks>
/// A21 handler VA <c>0111F800</c>: <c>u8 count</c>, then per entry <c>dstr name, u8 year-1900,
/// u8 month from 0, u8 day</c>. The name is read into a <c>0x1F</c> buffer, so only 1 to 30 bytes
/// are taken; outside that the client consumes the length alone and every later byte is misread.
/// Entries are merged into the client's list by name and nothing already there is removed. The client
/// handles this reply even when it asked for nothing, which is what lets it follow a full friend list.
/// </remarks>
public sealed record BlacklistListResponse(IReadOnlyList<BlacklistListEntry> Entries) : IGameCmdMessage
{
    public const int NameLimit = 30;

    public ServerMessageType MessageType => ServerMessageType.GameRequestBlacklist;
}

internal sealed class BlacklistListResponseEncoder : GameCmdMessageEncoder<BlacklistListResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(BlacklistListResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(BlacklistListResponse message)
    {
        if (message.Entries.Count > byte.MaxValue)
            throw new InvalidDataException($"A blacklist of {message.Entries.Count} entries does not fit its one-byte count.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)message.Entries.Count);
        foreach (var entry in message.Entries)
        {
            ChatDString.Require(entry.Name, 1, BlacklistListResponse.NameLimit, "blacklist entry name");
            var year = entry.Since.Year - 1900;
            if (year is < 0 or > 255)
                throw new InvalidDataException($"The year {entry.Since.Year} does not fit the client's year-1900 byte.");
            ChatDString.Write(writer, entry.Name);
            writer.Write((byte)year);
            writer.Write((byte)(entry.Since.Month - 1));
            writer.Write((byte)entry.Since.Day);
        }
        writer.Flush();
        return GameCmdEnvelope.Accept(stream.ToArray());
    }
}

/// <summary>A blacklist reply, and the relations it had to leave out.</summary>
/// <param name="Skipped">
/// Targets whose current name the client cannot read in this reply. Character creation keeps names
/// to 18 bytes, so these can only be rows from an older database; they are reported rather than
/// logged here because some callers (notification handlers) are given no logger.
/// </param>
internal sealed record BlacklistListing(BlacklistListResponse Response, IReadOnlyList<CharacterPublicIdentity> Skipped);

/// <summary>Builds the blacklist reply from the owner's current relations and the targets' current names.</summary>
internal static class BlacklistListBuilder
{
    /// <summary>The owner's blacklist as CMD 0x0078 carries it, in the relation service's order.</summary>
    /// <param name="zone">
    /// The server's local zone. The client rebuilds a calendar day with <c>mktime</c> and keeps no
    /// zone, so the day sent is the one the server's operator would call the day of adding.
    /// </param>
    /// <remarks>
    /// Read only. A command calls it inside a read-only unit; a notification handler may call it with
    /// no transaction, in which case the relations and the identities are two separate reads and a
    /// target that stopped being live in between is simply absent from the list. Bounded by
    /// the relation limit of 64, well inside the one-byte count.
    /// </remarks>
    public static async ValueTask<BlacklistListing> BuildAsync(CharacterId owner,
        ICharacterRelationQueryDomainService relations, ICharacterIdentityQueryDomainService identities,
        TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var listed = await relations.ListAsync(owner, CharacterRelationKind.Blacklist, cancellationToken).ConfigureAwait(false);
        if (listed.Count == 0) return new(new([]), []);
        var live = await identities.ReadAsync([.. listed.Select(relation => relation.Target)], cancellationToken).ConfigureAwait(false);
        var entries = new List<BlacklistListEntry>(listed.Count);
        var skipped = new List<CharacterPublicIdentity>();
        foreach (var relation in listed)
        {
            if (!live.TryGetValue(relation.Target, out var target)) continue;
            if (target.Name.Length is < 1 or > BlacklistListResponse.NameLimit)
            {
                skipped.Add(target);
                continue;
            }
            entries.Add(new(target.Name, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(relation.Since, zone).DateTime)));
        }
        return new(new(entries), skipped);
    }
}
