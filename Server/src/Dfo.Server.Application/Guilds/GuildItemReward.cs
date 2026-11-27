using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Guilds;

internal sealed record GuildItemReward(ItemDeliveryGrant Item, DateTimeOffset MailExpiresAt);
