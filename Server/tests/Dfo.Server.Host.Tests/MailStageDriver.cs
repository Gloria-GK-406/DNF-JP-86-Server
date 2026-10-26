using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>Real domain transactions for prepared-stage facts that an HTTP client cannot submit.</summary>
internal static class MailStageDriver
{
    internal static Task<GmMailQueryResult> Query(GmTestHost host, string secret, GmMailQuery kind, Guid id, GmMailInput input) =>
        InTransaction(host, domain => domain.QueryAsync(secret, kind, new(), id, input, default));
    internal static Task<GmMailSendResult> Send(GmTestHost host, string secret, GmMailPreparation preparation) =>
        InTransaction(host, domain => domain.SendAsync(secret, preparation, default));
    private static Task<T> InTransaction<T>(GmTestHost host, Func<IGmOfficialMailDomainService, ValueTask<T>> body) => TestGameplay.RunAsync(host.Services, async () =>
    {
        await using var transaction = host.Services.GetRequiredService<DomainTransactionScope>().Begin();
        try
        {
            var value = await body(host.Services.GetRequiredService<IGmOfficialMailDomainService>());
            await transaction.CommitAsync(default);
            return value;
        }
        catch (Exception error) { await transaction.AbortAsync(error); throw; }
    });
}
