using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

// The friend frames more than one place sends: the UnitedFriend slice after an add or a removal,
// character selection on entry, and the presence and refresh pushes to a character's observers.
// Layouts are the A21 client's (research/client-evidence.md of friend-relations-first-batch;
// protocol/game/server-to-client/NOTI-0x0111.md and NOTI-0x0112.md).

/// <summary>The server group every friend entry names.</summary>
/// <remarks>
/// The client looks a friend up in its character table by <c>(server, name)</c>, and a character's
/// own group comes from byte 55 of the USERINFO subtype-0 tail, which this server writes as 1; the
/// one captured add request also carries 1. Any other value would never match, and the presence line
/// would gain a server-name prefix.
/// </remarks>
internal static class FriendServerGroup
{
    public const byte Own = 1;
}

/// <summary>One friend as the full list shows it.</summary>
/// <param name="Channel">The channel the friend is on, or 0 when offline (Spec AM-2: same channel included).</param>
public sealed record FriendListEntry(ushort Channel, byte[] Name, byte Level, int Job, byte GrowType);

/// <summary>NOTI <c>0x0111</c> subcmd 0: the whole friend list, replacing what the client holds.</summary>
/// <remarks>
/// A21 handler VA <c>01161580</c> → <c>01A31EA0</c>: every node but a client-local temporary block is
/// dropped -- the blacklist included -- and the list is read anew, so whoever sends this sends the
/// blacklist after it (<see cref="FriendListRefresh"/>). Per entry the client reads
/// <c>u8 server, u16 ch, dstr name (1 to 255 bytes), u8 level, u32 job, u8 grow, u8 bl, u32 tail</c>,
/// and stops reading the list at the first name it already holds as a friend, so no name repeats.
/// <c>bl</c> is written 0 because the blacklist that follows sets the mark itself; <c>tail</c> is
/// read and never used.
/// </remarks>
public sealed record FriendListMessage(IReadOnlyList<FriendListEntry> Entries) : IGameNotiMessage
{
    public const int NameLimit = 255;

    /// <summary>An entry's bytes other than the name: server, ch, the name's length, level, job, grow, bl, tail.</summary>
    public const int EntryBytesBeyondName = 1 + 2 + 4 + 1 + 4 + 1 + 1 + 4;

    public ServerMessageType MessageType => ServerMessageType.GameFriendInfo;
}

/// <summary>NOTI <c>0x0111</c> subcmd 2: these friends are gone from the list.</summary>
/// <remarks>
/// A21 method VA <c>01A2E4B0</c>: per name <c>u8 server, dstr name (1 to 255 bytes)</c>; the client
/// shows a line for each and stops at the first name that is not a friend there.
/// </remarks>
public sealed record FriendRemovedMessage(IReadOnlyList<byte[]> Names) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameFriendInfo;
}

/// <summary>NOTI <c>0x0112</c>: a friend came online on a channel, or left (channel 0).</summary>
/// <remarks>
/// A21 handler VA <c>0116F000</c>: <c>u16 ch, u8 server, dstr name (1 to 255 bytes), u8 bl</c>. The
/// client overwrites the entry's channel and blacklist mark with these and prints a line whether or
/// not it holds the friend, so it goes to observers only, and <paramref name="Blacklisted"/> must be
/// whether the recipient blacklisted this character: a false 0 clears the recipient's own mark.
/// </remarks>
public sealed record FriendPresenceMessage(ushort Channel, byte[] Name, bool Blacklisted) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameFriendPresence;
}

internal sealed class FriendListEncoder : GameNotiMessageEncoder<FriendListMessage>
{
    private const uint FullSubcommand = 0;

    /// <summary>The tail the client reads into each entry and never uses.</summary>
    private const uint UnusedTail = 0;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FriendListMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(FriendListMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(FullSubcommand);
        writer.Write((uint)message.Entries.Count);
        foreach (var entry in message.Entries)
        {
            ChatDString.Require(entry.Name, 1, FriendListMessage.NameLimit, "friend list name");
            writer.Write(FriendServerGroup.Own);
            writer.Write(entry.Channel);
            ChatDString.Write(writer, entry.Name);
            writer.Write(entry.Level);
            writer.Write(entry.Job);
            writer.Write(entry.GrowType);
            writer.Write((byte)0);
            writer.Write(UnusedTail);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class FriendRemovedEncoder : GameNotiMessageEncoder<FriendRemovedMessage>
{
    private const uint RemovedSubcommand = 2;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FriendRemovedMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(FriendRemovedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(RemovedSubcommand);
        writer.Write((uint)message.Names.Count);
        foreach (var name in message.Names)
        {
            ChatDString.Require(name, 1, FriendListMessage.NameLimit, "removed friend name");
            writer.Write(FriendServerGroup.Own);
            ChatDString.Write(writer, name);
        }
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class FriendPresenceEncoder : GameNotiMessageEncoder<FriendPresenceMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FriendPresenceMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(FriendPresenceMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ChatDString.Require(message.Name, 1, FriendListMessage.NameLimit, "friend presence name");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Channel);
        writer.Write(FriendServerGroup.Own);
        ChatDString.Write(writer, message.Name);
        writer.Write(message.Blacklisted ? (byte)1 : (byte)0);
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>A full friend list, and the friends it had to leave out.</summary>
/// <param name="Skipped">
/// Live friends the client could not take in this list: a name outside 1 to 255 bytes, a name already
/// listed, or a level or grow type outside a byte. Only an older database can hold such rows; they are
/// reported rather than logged here because notification handlers are given no logger.
/// </param>
internal sealed record FriendListing(FriendListMessage Message, IReadOnlyList<CharacterPublicIdentity> Skipped);

/// <summary>Builds the full friend list from the owner's current relations and the friends' current facts.</summary>
internal static class FriendListBuilder
{
    /// <summary>The owner's friends as NOTI 0x0111 subcmd 0 carries them, in the relation service's order.</summary>
    /// <remarks>
    /// Read only, and its dependencies are all domain services, so a command calls it inside a
    /// read-only unit and a notification handler may call it with none; then relations, identities
    /// and presence are separate reads, and a friend that changed in between shows its later state.
    /// A friend online anywhere carries its channel's number, the owner's own channel included
    /// (Spec AM-2); offline is 0. Bounded by the relation limit of 64: at most <c>8 + 64 x 273</c> bytes.
    /// </remarks>
    public static async ValueTask<FriendListing> BuildAsync(CharacterId owner,
        ICharacterRelationQueryDomainService relations, ICharacterIdentityQueryDomainService identities,
        ISelectedSessionDomainService sessions, IChannelEntryDomainService channels, CancellationToken cancellationToken)
    {
        var listed = await relations.ListAsync(owner, CharacterRelationKind.Friend, cancellationToken).ConfigureAwait(false);
        if (listed.Count == 0) return new(new([]), []);
        var live = await identities.ReadAsync([.. listed.Select(relation => relation.Target)], cancellationToken).ConfigureAwait(false);
        var entries = new List<FriendListEntry>(listed.Count);
        var skipped = new List<CharacterPublicIdentity>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var channelOf = new Dictionary<int, ushort>();
        foreach (var relation in listed)
        {
            if (!live.TryGetValue(relation.Target, out var friend)) continue;
            if (friend.Name.Length is < 1 or > FriendListMessage.NameLimit
                || friend.Level is < 0 or > byte.MaxValue
                || friend.GrowType is < 0 or > byte.MaxValue
                || !names.Add(Convert.ToHexString(friend.Name)))
            {
                skipped.Add(friend);
                continue;
            }

            ushort channel = 0;
            if (sessions.TryGetInWorld(friend.Character, out var session))
            {
                if (!channelOf.TryGetValue(session.ListenerPort, out channel))
                {
                    var resolved = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
                    channel = resolved.ChannelId is >= 0 and <= ushort.MaxValue
                        ? (ushort)resolved.ChannelId
                        : throw new InvalidDataException($"Channel {resolved.ChannelId} does not fit the friend list's two-byte channel.");
                    channelOf[session.ListenerPort] = channel;
                }
            }
            entries.Add(new(channel, friend.Name, (byte)friend.Level, friend.Job, (byte)friend.GrowType));
        }
        return new(new(entries), skipped);
    }
}

/// <summary>The full friend list and the blacklist that has to follow it to the same recipient.</summary>
/// <remarks>
/// A full list wipes the client's blacklist (RQ-004, Spec RQ-036), so the two always travel together
/// and in this order: <see cref="Messages"/> for a notification handler to return,
/// <see cref="RegisterAsync"/> for a command, and character selection hands it to the entry sequence.
/// </remarks>
/// <param name="SkippedFriends">Friends left out of <paramref name="Friends"/>; see <see cref="FriendListing"/>.</param>
/// <param name="SkippedBlacklist">Entries left out of <paramref name="Blacklist"/>; see <see cref="BlacklistListing"/>.</param>
public sealed record FriendListRefresh(
    FriendListMessage Friends,
    BlacklistListResponse Blacklist,
    IReadOnlyList<CharacterPublicIdentity> SkippedFriends,
    IReadOnlyList<CharacterPublicIdentity> SkippedBlacklist)
{
    /// <summary>A character that keeps no friend and no blacklist.</summary>
    public static FriendListRefresh None { get; } = new(new([]), new([]), [], []);

    /// <summary>The two frames in the order the recipient must receive them.</summary>
    public IReadOnlyList<IGameServerMessage> Messages => [Friends, Blacklist];

    /// <summary>Reads both lists of <paramref name="owner"/>. Read only, like the two builders it joins.</summary>
    /// <param name="zone">The server's local zone, for the blacklist's dates; see <see cref="BlacklistListBuilder"/>.</param>
    internal static async ValueTask<FriendListRefresh> BuildAsync(CharacterId owner,
        ICharacterRelationQueryDomainService relations, ICharacterIdentityQueryDomainService identities,
        ISelectedSessionDomainService sessions, IChannelEntryDomainService channels, TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var friends = await FriendListBuilder.BuildAsync(owner, relations, identities, sessions, channels, cancellationToken).ConfigureAwait(false);
        var blacklist = await BlacklistListBuilder.BuildAsync(owner, relations, identities, zone, cancellationToken).ConfigureAwait(false);
        return new(friends.Message, blacklist.Response, friends.Skipped, blacklist.Skipped);
    }

    /// <summary>Registers the two frames, in order, for one connection of the running command.</summary>
    internal async ValueTask RegisterAsync(IGameCommandOutbox outbox, NetworkConnectionId recipient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        await outbox.RegisterNotiAsync(recipient, Friends, cancellationToken).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(recipient, Blacklist, cancellationToken).ConfigureAwait(false);
    }
}
