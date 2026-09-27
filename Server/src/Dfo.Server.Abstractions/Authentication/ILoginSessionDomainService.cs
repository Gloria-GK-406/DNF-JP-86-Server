using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Authentication;

public interface ILoginSessionDomainService
{
    /// <summary>Revoke the credential owned by this exact connection; leave exit flush traffic intact.</summary>
    void EndSession(NetworkConnectionId connectionId);
}
