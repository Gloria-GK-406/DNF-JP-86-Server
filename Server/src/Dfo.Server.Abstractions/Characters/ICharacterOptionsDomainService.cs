using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Characters;
public interface ICharacterOptionsDomainService
{
    ValueTask<bool> SaveAsync(SelectedSessionIdentity session, byte[] body, CancellationToken cancellationToken);
}
