using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildGift(GuildId Sender, string SenderName, GuildId Recipient, string RecipientName,
    GuildContentDefinition Content, long SentAt, long? ClaimedAt = null);

public readonly record struct GuildGiftKey(GuildId Sender, ushort ProductId, byte Kind, uint SentAt);

public enum GuildGiftStatus
{
    Success,
    LeaderRequired,
    AllyRequired,
    UnsupportedContent,
    DailyLimit,
    InsufficientPoints,
    MissingGift,
    ActivationUnavailable,
}

public sealed record GuildGiftResult(GuildGiftStatus Status, GuildGift? Gift = null, CharacterId? RecipientLeader = null);

public sealed record GuildGiftView(IReadOnlyList<GuildGift> Rows);
