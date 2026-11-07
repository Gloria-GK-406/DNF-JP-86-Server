using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Tickets 2 and 3, the elementary and advanced dragon-leap breath scrolls (10012055 / 10012056):
/// a dragon-leap weapon whose scroll star is full becomes its next star.
/// </summary>
/// <remarks>
/// The block is the one declaring <c>[equipment grow]</c>. Admission mirrors the A21 client's own
/// check (VA 0x02419450): not on a star that grows by experience (1 and 2), not on the last star,
/// and only with the star's experience full. The scrolls' own text decides what is kept: the
/// elementary one says reinforcement, amplification, forging and enchantment all disappear, the
/// advanced one that they stay. Either way the new star starts at zero experience.
/// </remarks>
internal sealed class DragonLeapEmancipatePolicy(IEquipmentGrowthRuleService growth) : IEquipmentEmancipatePolicy
{
    public const int Elementary = 2;
    public const int Advanced = 3;
    public IReadOnlySet<int> TicketTypes { get; } = new HashSet<int> { Elementary, Advanced };

    public EquipmentEmancipateDefinition? Select(EmancipateTicketDefinition ticket, IReadOnlyList<EquipmentEmancipateDefinition> blocks)
    {
        var matching = blocks.Where(x => x.Growth is not null).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    public async ValueTask<EquipmentEmancipateRejection> CheckAsync(EmancipateTicketDefinition ticket, ItemSnapshot target,
        CancellationToken cancellationToken)
    {
        var rule = await growth.ForAsync(target.ItemId, cancellationToken).ConfigureAwait(false);
        return rule is null || rule.GrowsByExperience || rule.IsLastStar || target.GrowthExperience < rule.MaxExperience
            ? EquipmentEmancipateRejection.GrowthIncomplete : EquipmentEmancipateRejection.None;
    }

    public ItemSnapshot Retain(EmancipateTicketDefinition ticket, ItemSnapshot before, ItemSnapshot transformed)
    {
        var started = transformed with { GrowthExperience = 0 };
        return ticket.TicketType == Advanced ? started : started with
        {
            Attribute = (byte)(started.Attribute & 0xE0),
            AmplifyType = 0,
            AmplifyValue = 0,
            GenuineUpgrade = 0,
            EnchantCardId = 0,
            EnchantUpgradeCount = 0,
        };
    }
}
