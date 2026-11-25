using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Pvp;

public interface IPvpTotalMatchRepresentativeSwitchDomainService
{
    void Expect(NetworkConnectionContext connection, AccountId account, byte rosterIndex);
    void PublishRoster(NetworkConnectionContext connection, AccountId account);
    bool HasPublishedRoster(NetworkConnectionContext connection, AccountId account);
    bool Consume(NetworkConnectionContext connection, AccountId account, ushort rosterIndex);
}
