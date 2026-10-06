using Dfo.Server.Data.Accounts;
namespace Dfo.Server.Abstractions.Characters;

/// <summary>Account-owned honour and growth capsule settlement in the caller's transaction.</summary>
public interface IExperienceCapsuleDomainService
{
    ValueTask<DungeonHonourOutcome> AddAsync(AccountId account, uint experience, CancellationToken ct);
}
