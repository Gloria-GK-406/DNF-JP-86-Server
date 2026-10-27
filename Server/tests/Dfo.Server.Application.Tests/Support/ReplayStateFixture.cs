using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;

// Direct snapshot/projection fixture. Protocol admission and committed replay are tested through the real bus elsewhere.
internal static class ReplayStateFixture
{
    internal static ValueTask<ReplayStateSnapshot> ReadAsync(IServiceProvider services,SelectedSessionIdentity session,ReplayProjectionRequest request,GameMessageMetadata? metadata,CancellationToken ct=default)=>
        new(TestGameplay.RunAsync(services,async()=>{
            var safety=services.GetRequiredService<SelectedSessionSafety>();var recipient=safety.Capture(session,metadata);
            await using var tx=services.GetRequiredService<DomainTransactionScope>().Begin();
            var snapshot=await services.GetRequiredService<IReplayStateDomainService>().ReadAsync(session,request,metadata,ct);
            safety.DemandCurrent(recipient);await tx.CommitAsync(ct);return snapshot;
        }));
    internal static async ValueTask RefreshAsync(IServiceProvider services,NetworkConnectionContext connection,SelectedSessionIdentity session,ReplayProjectionRequest request,GameMessageMetadata? metadata,CancellationToken ct=default)
    {
        var safety=services.GetRequiredService<SelectedSessionSafety>();var recipient=safety.Capture(session,metadata);
        if(!ReferenceEquals(connection,recipient.Connection))throw new InvalidOperationException("Replay requires its exact original connection.");
        var snapshot=await ReadAsync(services,session,request,metadata,ct);safety.DemandCurrent(recipient);
        await services.GetRequiredService<IReplayStateProjectionSink>().ProjectAsync(connection,session,metadata,snapshot,null,ct);
    }
}
