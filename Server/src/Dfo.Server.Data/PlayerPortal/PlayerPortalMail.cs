using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dfo.Server.Data.PlayerPortal;

/// <summary>
/// One attachment of a player's mail: an item and how many, nothing more. Any other field (equipment options of any
/// kind) is refused when the request is read (RQ-011).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlayerMailItem([property: JsonRequired] int ItemId, [property: JsonRequired] int Count);

/// <summary>A player's mail to one character of the own account; the account is always the session's (RQ-003).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlayerMailRequest([property: JsonRequired] Guid RequestId, [property: JsonRequired] int CharacterId,
    [property: JsonRequired] string Title, [property: JsonRequired] string Body, [property: JsonRequired] PlayerMailItem[] Items,
    [property: JsonRequired] int Gold);

/// <summary>One attachment as it was sent: <see cref="Kind"/> is <c>equipment</c> or <c>stackable</c>.</summary>
public sealed record PlayerMailReceiptItem(int ItemId, int Count, string? Name, string Kind);

/// <summary>
/// The stored receipt of a sent player mail. It starts with the five fields every player receipt has
/// (<see cref="RequestId"/>, <see cref="Kind"/>, <see cref="CommittedAt"/>, <see cref="Summary"/>, <see cref="Result"/>);
/// <see cref="Body"/> is the body as delivered, the GM default text when it was left blank.
/// </summary>
public sealed record PlayerMailReceipt(Guid RequestId, string Kind, DateTimeOffset CommittedAt, string Summary, string Result,
    int CharacterId, string CharacterName, string Title, string Body, IReadOnlyList<PlayerMailReceiptItem> Items, int Gold,
    DateTimeOffset ExpiresAt);

/// <summary>A sent mail's answer: the stored receipt as it was stored, and whether this request only replayed it.</summary>
public sealed record PlayerMailSendResult(JsonElement Receipt, bool Replayed);
