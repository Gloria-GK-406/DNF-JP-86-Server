using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Persistence;

public interface IPremiumServiceUsageTable
{
    ValueTask<int> ReadAsync(CharacterId id, string key, string period, CancellationToken token);
    ValueTask<int> TryIncrementAsync(CharacterId id, string key, string period, int limit, CancellationToken token);
}
