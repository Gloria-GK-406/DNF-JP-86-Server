namespace Dfo.Server.Abstractions.Effects;

public sealed record SetPurchaseRestrictionEffect(int RestrictionId, int? ExpectedEnd, int End) : DomainEffect;
public sealed record PurchaseRestrictionChange(int RestrictionId, int End) : EffectChange;
