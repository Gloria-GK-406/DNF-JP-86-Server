namespace Dfo.Server.Data.Accounts;

public readonly record struct AccountId(int Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record AccountRecord(
    AccountId AccountId,
    string LoginId,
    string PasswordHash,
    DateTimeOffset CreatedAt);
