using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Effects;

public sealed record GrantPreparedItemEffect(OpeningPreparedGrant Grant, ItemDeliveryMode DeliveryMode) : InventoryEffect;
public sealed record GrantOpeningGoldEffect(int Amount) : InventoryEffect;
public sealed record GrantOpeningVirtualEffect(int Slot, int CoreItemId, byte ItemKind, OpeningReward Reward) : InventoryEffect;
public sealed record SetOpeningPremiumEffect(PremiumServiceType Type, long ExpectedExpiry, long NextExpiry, OpeningReward Reward) : InventoryEffect;
public sealed record SetSeriaLuckEffect(int Expected, int Next) : InventoryEffect;
public sealed record SetDoublePotUsageEffect(DoublePotUsageDecision Decision) : InventoryEffect;
public sealed record PreparedItemGrantChange(ImmutableArray<OpeningRewardDisplay> Rewards, ImmutableArray<BusinessSlot> Slots) : EffectChange;
public sealed record OpeningVirtualChange(BusinessSlot Slot, ItemSnapshot Core, OpeningRewardDisplay Reward) : EffectChange;
public sealed record OpeningPremiumChange(AccountPremiumEntryState Premium, OpeningRewardDisplay Reward) : EffectChange;
public sealed record SeriaLuckChange(int Before, int After) : EffectChange;
public sealed record DoublePotUsageChange(DoublePotUsageDecision Decision) : EffectChange;
