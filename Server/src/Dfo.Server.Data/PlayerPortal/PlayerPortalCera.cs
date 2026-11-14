using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dfo.Server.Data.PlayerPortal;

/// <summary>A player's request to add cera to the own account; the account is always the session's (RQ-003).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlayerCeraRequest([property: JsonRequired] Guid RequestId, [property: JsonRequired] string Currency,
    [property: JsonRequired] int Amount);

/// <summary>
/// The stored receipt of a succeeded cera grant. Every player receipt starts with the same five fields
/// (<see cref="RequestId"/>, <see cref="Kind"/>, <see cref="CommittedAt"/>, <see cref="Summary"/>, <see cref="Result"/>),
/// which is all the record page reads; the rest is the cera grant's own.
/// </summary>
public sealed record PlayerCeraReceipt(Guid RequestId, string Kind, DateTimeOffset CommittedAt, string Summary, string Result,
    string Currency, int Amount, PlayerBalancesView Before, PlayerBalancesView After);

/// <summary>A cera grant's answer: the stored receipt as it was stored, and whether this request only replayed it.</summary>
public sealed record PlayerCeraGrantResult(JsonElement Receipt, bool Replayed);

/// <summary>One page of the player's own receipts, newest first, each exactly as it was stored.</summary>
public sealed record PlayerOperationsView(IReadOnlyList<JsonElement> Items, long Total, int Page, int PageSize);
