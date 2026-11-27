using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Guilds;

internal sealed record GuildContributionReward(ItemDeliveryGrant Item, DateTimeOffset MailExpiresAt);
