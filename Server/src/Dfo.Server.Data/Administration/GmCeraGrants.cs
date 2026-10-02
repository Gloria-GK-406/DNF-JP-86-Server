using System.Text.Json.Serialization;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Administration;

public sealed record GmCeraInput(int[] AccountIds, string Currency, int Amount, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GmCeraRequest([property: JsonRequired] int[] AccountIds, [property: JsonRequired] string Currency,
    [property: JsonRequired] int Amount, [property: JsonRequired] string Reason, Guid RequestId = default);
public sealed record GmCeraAccountIdentity(int AccountId, string LoginName);
public sealed record GmCeraAccount(int AccountId, string LoginName, AccountCeraBalances Balances);
public sealed record GmCeraBalanceChange(int AccountId, string LoginName, AccountCeraBalances Before, AccountCeraBalances After);
public sealed record GmCeraDecision(AccountCeraBalances Before, AccountCeraBalances After);
public sealed record GmCeraPreview(GmCeraInput Input, IReadOnlyList<GmCeraBalanceChange> Accounts, long TotalAmount);
public sealed record GmCeraReceipt(Guid RequestId, long AdministratorId, string AdministratorName, DateTimeOffset CommittedAt,
    GmCeraInput Input, IReadOnlyList<GmCeraBalanceChange> Accounts);
public sealed record GmCeraOperation(string PayloadHash, GmCeraReceipt Receipt);
public sealed record GmCeraGrantResult(GmCeraReceipt Receipt, bool Replayed);
public sealed record GmCeraQueryResult(object Value);
public enum GmCeraQuery { Accounts, Preview, Normalize, Operations, Operation }
public enum GmCeraRefusal { Invalid, NotFound, Conflict, Unauthorized, Forbidden }
public sealed class GmCeraException(GmCeraRefusal reason, string message) : InvalidOperationException(message)
{
    public GmCeraRefusal Reason { get; } = reason;
}
