using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using System.Text.Json.Serialization;

namespace Dfo.Server.Data.Administration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GmMailItem([property: JsonRequired] int ItemId, [property: JsonRequired] int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ItemEquipmentOptions? Equipment = null);
public sealed record GmMailInput(string Title, string Body, int[] RecipientIds, GmMailItem[] Items, int Gold);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GmMailEditorRequest(string Title, string Body, int[] RecipientIds, GmMailItem[] Items, [property: JsonRequired] int Gold,
    long? ExpectedRevision = null, string? Name = null, Guid RequestId = default);
public sealed record GmMailDocument(Guid Id, string Name, long Revision, GmMailInput Input);
public sealed record GmMailRecipient(int CharacterId, string Name, string NameDecodeStatus, string? NameBytesHex,
    int Level, int Job, int GrowType);
public sealed record GmMailRecipientRecord(int CharacterId, int AccountId, byte[]? NameBytes, int Level, int Job, int GrowType);
public sealed record GmMailItemPreview(int ItemId, int Count, string Kind, ushort Durability, bool Sealed,
    string? Name = null, byte UpgradeLevel = 0, byte AmplifyType = 0, ushort AmplifyValue = 0);
public sealed record GmMailPreview(GmMailInput Input, string DisplayText, int Utf8Bytes,
    IReadOnlyList<GmMailRecipient> Recipients, IReadOnlyList<GmMailItemPreview> Items, long TotalGold, long TotalItems);
public sealed record GmMailSentRecipient(GmMailRecipient Character, int AccountId, long MailId, int[] EquipmentUids);
public sealed record GmMailReceipt(Guid RequestId, long AdministratorId, string AdministratorName,
    DateTimeOffset CommittedAt, DateTimeOffset ExpiresAt, GmMailInput Input, IReadOnlyList<GmMailSentRecipient> Recipients,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GmMailItemPreview>? Items = null);
public sealed record GmMailOperation(string PayloadHash, GmMailReceipt Receipt);
public sealed record GmMailSendResult(GmMailReceipt Receipt, bool Replayed);
public sealed record GmMailPreparation(Guid RequestId, GmMailInput Input, IReadOnlyList<AccountId> Accounts);
public sealed record GmMailPreparedItem(GmMailItem Requested, GmMailItemPreview Preview, ItemSnapshot Snapshot);
public sealed record GmMailQueryResult(object Value);
public enum GmMailQuery { Draft, Templates, Recipients, Preview, Prepare, Operations, Operation }
public enum GmMailEdit { Draft, CreateTemplate, UpdateTemplate, DeleteTemplate }
public enum GmMailRefusal { Invalid, NotFound, Conflict, Unauthorized, Forbidden, Changed }
public sealed class GmMailException(GmMailRefusal reason, string message) : InvalidOperationException(message)
{
    public GmMailRefusal Reason { get; } = reason;
}
