using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Chat;

namespace Dfo.Server.Abstractions.Chat;

/// <summary>Who a chat line is meant for, named by what the group is rather than by who is in it.</summary>
/// <remarks>
/// A scope holds no member list. The members of every scope are kept by that group's own owner --
/// online sessions, town areas, parties, conversations -- and are asked of it at the moment a line is posted, so
/// chat can never disagree with them.
/// <para>
/// The private constructor keeps the ordinary shapes inside this file, though it does not seal the
/// hierarchy: a record's generated copy constructor is protected, so a derived record elsewhere still
/// compiles. What protects delivery is <see cref="IChatScopeDomainService.MembersOf"/>, which fails on
/// a scope it was not taught instead of reaching nobody.
/// </para>
/// </remarks>
public abstract record ChatScope
{
    private ChatScope()
    {
    }

    /// <summary>The party the anchor is in. A character in no real party has none.</summary>
    public sealed record PartyOf(CharacterId Anchor) : ChatScope;

    /// <summary>The town area the anchor stands in. Away from town there is none.</summary>
    public sealed record AreaOf(CharacterId Anchor) : ChatScope;

    /// <summary>The channel the anchor is playing on.</summary>
    public sealed record ChannelOf(CharacterId Anchor) : ChatScope;

    /// <summary>The anchor's guild. No guild system exists yet, so it never has members.</summary>
    public sealed record GuildOf(CharacterId Anchor) : ChatScope;

    /// <summary>Every channel of this server.</summary>
    public sealed record Server : ChatScope;

    /// <summary>One character.</summary>
    public sealed record Character(CharacterId Target) : ChatScope;

    /// <summary>The members of a 1:1 conversation that is going on.</summary>
    public sealed record Conversation(ChatConversationId Id) : ChatScope;
}
