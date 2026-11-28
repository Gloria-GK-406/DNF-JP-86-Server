using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Pvp;

/// <summary>The next selection expected after A21 saves a different total-match representative.</summary>
public sealed class PvpTotalMatchRepresentativeSwitchDomainService : IPvpTotalMatchRepresentativeSwitchDomainService
{
    private sealed record Pending(AccountId Account, ushort RosterIndex, bool RosterPublished = false);

    // A connection that closes before its next selection does not retain a transition.
    private readonly ConditionalWeakTable<NetworkConnectionContext, Pending> pending = new();

    public void Expect(NetworkConnectionContext connection, AccountId account, byte rosterIndex)
    {
        GameplayWriteAssertion.Writing(nameof(PvpTotalMatchRepresentativeSwitchDomainService) + "." + nameof(Expect));
        pending.AddOrUpdate(connection, new Pending(account, rosterIndex));
    }

    public void PublishRoster(NetworkConnectionContext connection, AccountId account)
    {
        GameplayWriteAssertion.Writing(nameof(PvpTotalMatchRepresentativeSwitchDomainService) + "." + nameof(PublishRoster));
        if (pending.TryGetValue(connection, out var expected) && expected.Account == account)
            pending.AddOrUpdate(connection, expected with { RosterPublished = true });
    }

    public bool HasPublishedRoster(NetworkConnectionContext connection, AccountId account) =>
        pending.TryGetValue(connection, out var expected) && expected.Account == account && expected.RosterPublished;

    public bool Consume(NetworkConnectionContext connection, AccountId account, ushort rosterIndex)
    {
        GameplayWriteAssertion.Writing(nameof(PvpTotalMatchRepresentativeSwitchDomainService) + "." + nameof(Consume));
        if (!pending.TryGetValue(connection, out var expected)) return false;
        pending.Remove(connection);
        return expected.Account == account && expected.RosterIndex == rosterIndex && expected.RosterPublished;
    }
}
