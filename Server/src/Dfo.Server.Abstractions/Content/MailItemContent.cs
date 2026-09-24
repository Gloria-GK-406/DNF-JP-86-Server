namespace Dfo.Server.Abstractions.Content;

/// <summary>The PVF attach declaration, kept distinct from an instance's transfer state.</summary>
public enum MailItemAttachType
{
    Unknown,
    Free,
    /// <summary>PVF token [trade]; the reference server treats it as non-transferable.</summary>
    Trade,
    TradeDelete,
    Sealing,
    SealingTrade,
    Account,
    TradeLimit,
}

public sealed record MailItemDefinition(
    MailItemAttachType AttachType,
    string RawAttachType,
    int? TradeLimitMax);

public interface IMailItemDefinitionSource
{
    /// <summary>Null when the template is not declared or its file cannot be read.</summary>
    ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
