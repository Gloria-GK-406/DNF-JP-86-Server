using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum MailItemSendRefusal
{
    None,
    InvalidItem,
    InstanceLocked,
    InstanceTradeRestricted,
    UnknownTemplate,
    TemplateTradeRestricted,
    AccountBound,
    Unsealed,
    UnsupportedTradeLimit,
}

public sealed record MailItemSendDecision(MailItemSendRefusal Refusal)
{
    public bool Allowed => Refusal == MailItemSendRefusal.None;
}

/// <summary>Decides whether a player may transfer one held item through mail.</summary>
public interface IMailItemRuleService
{
    /// <param name="sameAccount">A verified account relationship, not a caller-supplied claim.</param>
    ValueTask<MailItemSendDecision> CanSendAsync(
        ItemSnapshot core, bool sameAccount, CancellationToken cancellationToken);
}
