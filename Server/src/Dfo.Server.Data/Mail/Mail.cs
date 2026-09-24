using System.Collections.Immutable;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Mail;

public enum MailFailure { InvalidMail, CharacterNotFound, InvalidItem, NotOwned, NotSendable, InsufficientAssets, NotFound, Expired, AlreadyClaimed, UnclaimedAttachments, NoCapacity, GoldLimit, ConcurrentChange, LegacyAttachment }
public sealed class MailException(MailFailure reason, string message) : InvalidOperationException(message)
{
    public MailFailure Reason { get; } = reason;
}

public sealed record MailSource(CharacterId? Character, string SystemName)
{
    public static MailSource FromCharacter(CharacterId id) => new(id, string.Empty);
    public static MailSource FromSystem(string name) => new(null, name);
}

public abstract record MailAsset
{
    private MailAsset() { }
    public sealed record Equipment(ItemIdentity Identity) : MailAsset;
    public sealed record Stackable(int ItemId, int Count) : MailAsset;
    public sealed record Gold(int Amount) : MailAsset;
}

public sealed record Mail
{
    internal Mail(MailSource source, CharacterId recipient, string title, string body,
        DateTimeOffset? expiresAt, ImmutableArray<MailAsset> attachments)
    { Source = source; Recipient = recipient; Title = title; Body = body; ExpiresAt = expiresAt; Attachments = attachments; }
    public MailSource Source { get; }
    public CharacterId Recipient { get; }
    public string Title { get; }
    public string Body { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public ImmutableArray<MailAsset> Attachments { get; }
}

public sealed class MailBuilder(MailSource source, CharacterId recipient)
{
    private readonly List<MailAsset> attachments = [];
    private string title = string.Empty;
    private string body = string.Empty;
    private DateTimeOffset? expiresAt;
    public MailBuilder WithText(string subject, string content) { title = subject; body = content; return this; }
    public MailBuilder ExpiresAt(DateTimeOffset value) { expiresAt = value; return this; }
    public MailBuilder AddEquipment(ItemIdentity identity) { attachments.Add(new MailAsset.Equipment(identity)); return this; }
    public MailBuilder AddStackable(int itemId, int count) { attachments.Add(new MailAsset.Stackable(itemId, count)); return this; }
    public MailBuilder AddGold(int amount) { attachments.Add(new MailAsset.Gold(amount)); return this; }
    public Mail Build()
    {
        if (source is null || recipient.Value <= 0 || title is null || body is null
            || (source.Character is { } id ? id.Value <= 0 || source.SystemName != string.Empty : string.IsNullOrWhiteSpace(source.SystemName)))
            throw new MailException(MailFailure.InvalidMail, "Mail requires a valid source, recipient and text.");
        foreach (var asset in attachments)
        {
            if (asset switch { MailAsset.Equipment e => e.Identity.Value <= 0,
                MailAsset.Stackable s => s.ItemId <= 0 || s.Count <= 0,
                MailAsset.Gold g => g.Amount <= 0, _ => true })
                throw new MailException(MailFailure.InvalidMail, "Attachment identifiers and amounts must be positive.");
        }
        if (attachments.OfType<MailAsset.Equipment>().Select(x => x.Identity).Distinct().Count() != attachments.OfType<MailAsset.Equipment>().Count())
            throw new MailException(MailFailure.InvalidMail, "An equipment instance cannot occur twice.");
        if (attachments.OfType<MailAsset.Gold>().Sum(x => (long)x.Amount) > int.MaxValue)
            throw new MailException(MailFailure.InvalidMail, "Total attached gold exceeds the supported balance.");
        return new Mail(source, recipient, title, body, expiresAt, attachments.ToImmutableArray());
    }
}

public sealed record MailAttachment(long Id, MailAsset Asset, bool Claimed, bool LegacyUnsupported = false);
public sealed record MailView(long Id, CharacterId Recipient, int SenderCharacterId, string SenderName,
    string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, bool Read,
    ImmutableArray<MailAttachment> Attachments);
public sealed record MailAssetChange(CharacterId Character, InventoryContainerType? Container, int? SlotIndex, int ItemId, int Count, int? GoldBalance)
{
    public AccountMaterialChange? AccountMaterial { get; init; }
}
public sealed record MailSendReceipt(long MailId, ImmutableArray<MailAssetChange> Changes);
public sealed record MailClaimReceipt(long MailId, long AttachmentId, ImmutableArray<MailAssetChange> Changes);

public sealed record MailHeader(long Id, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record MailPage(int TotalCount, IReadOnlyList<MailView> Messages);
