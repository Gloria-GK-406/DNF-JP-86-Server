using Dfo.Server.Data.Administration;

namespace Dfo.Server.Abstractions.Administration;

public interface IGmPlayerDirectoryPersistence
{
    ValueTask<GmStoredPage<GmAccountRecord>> AccountsAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token);
    ValueTask<GmStoredPage<GmCharacterRecord>> CharactersAsync(GmPlayerFilter filter, IReadOnlyCollection<int>? onlineIds, CancellationToken token);
    ValueTask<(long Accounts, long Characters)> CountsAsync(CancellationToken token);
}

public interface IGmPlayerDirectoryDomainService
{
    ValueTask<GmPage<GmAccountView>> AccountsAsync(GmPlayerFilter filter, CancellationToken token);
    ValueTask<GmPage<GmCharacterView>> CharactersAsync(GmPlayerFilter filter, CancellationToken token);
    ValueTask<GmAccountLookup> AccountAsync(int id, CancellationToken token);
    ValueTask<GmCharacterLookup> CharacterAsync(int id, CancellationToken token);
    ValueTask<GmOverview> OverviewAsync(CancellationToken token);
}
