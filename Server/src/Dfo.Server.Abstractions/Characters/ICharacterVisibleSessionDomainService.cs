using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterVisibleSessionDomainService
{
    IReadOnlyList<SelectedSessionIdentity> Resolve(SelectedSessionIdentity owner);
}
