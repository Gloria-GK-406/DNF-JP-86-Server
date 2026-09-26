using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Accounts;

public interface ICrystalContractDomainService
{
    ValueTask<bool> SelectAsync(SelectedSessionIdentity session, byte mode, byte selection, CancellationToken ct);
}
