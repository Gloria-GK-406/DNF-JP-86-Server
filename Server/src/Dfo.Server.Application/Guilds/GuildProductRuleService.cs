using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildProductRuleService(IGuildEconomySource content)
{
    public GuildContentDefinition? FindProduct(uint productId) =>
        content.ReadContentCatalog()?.Products.GetValueOrDefault(productId);

    public GuildGoldRenameOffer? GoldRenameOffer(uint productId) =>
        content.ReadContentCatalog()?.GoldRename is { GoldCost: > 0 } offer && offer.ProductId == productId ? offer : null;

    public int? WarehouseCapacityLimit() => content.ReadContentCatalog()?.WarehouseCapacityLimit;

    public GuildBenefitGrantDecision EvaluateGrant(
        GuildContentDefinition product, GuildPurchasedContent? existing, DateTimeOffset now)
    {
        var uses = existing?.RemainingUses ?? 0;
        long expiry = 0;
        switch (product.Kind)
        {
            case GuildContentKind.PermanentAttributes:
                if (existing is not null)
                    return new(GuildMutationStatus.ContentAlreadyActive);
                if (Attributes(product.Kind, product.Value) is null)
                    return new(GuildMutationStatus.UnsupportedContent);
                break;
            case GuildContentKind.TemporaryAttributes:
                if (Attributes(product.Kind, product.Value) is null)
                    return new(GuildMutationStatus.UnsupportedContent);
                goto case GuildContentKind.Experience;
            case GuildContentKind.Experience:
                var start = Math.Max(now.ToUnixTimeSeconds(), existing?.ExpiresAtUnixSeconds ?? 0);
                var duration = product.DurationDays * 86_400L;
                // A21 reserves int.MaxValue for permanent benefits.
                if (duration <= 0 || start >= int.MaxValue - duration)
                    return new(GuildMutationStatus.ContentDurationLimit);
                expiry = start + duration;
                break;
            case GuildContentKind.PartyTeleport:
                if (uses > int.MaxValue - product.Value)
                    return new(GuildMutationStatus.ContributionLimit);
                uses += product.Value;
                break;
            default:
                return new(GuildMutationStatus.UnsupportedContent);
        }

        return new(GuildMutationStatus.Success, new(product.Kind, product.ProductId, product.Value, uses, expiry));
    }

    public GuildAttributeBonus? Attributes(GuildContentKind kind, int buffId)
    {
        var catalog = content.ReadContentCatalog();
        var bonus = kind switch
        {
            GuildContentKind.PermanentAttributes => catalog?.Attributes,
            GuildContentKind.TemporaryAttributes => catalog?.TemporaryAttributes,
            _ => null,
        };
        return bonus?.BuffId == buffId ? bonus : null;
    }
}

internal readonly record struct GuildBenefitGrantDecision(GuildMutationStatus Status, GuildPurchasedContent? Content = null);
