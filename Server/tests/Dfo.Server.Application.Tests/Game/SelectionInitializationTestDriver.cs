using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Game;
internal static class SelectionInitializationTestDriver
{
    internal static Task InitializeAsync(IServiceProvider services, SelectedSessionIdentity session) => TestGameplay.RunAsync(async () =>
    {
        await using var unit = services.GetRequiredService<DomainTransactionScope>().Begin();
        await Dfo.Server.Protocol.Game.SelectCharacter.GameSelectCharacterCommandHandler.InitializeHousingStageAsync(
            services.GetRequiredService<ISelectedSessionDomainService>(),services.GetRequiredService<IMarriageHousingDomainService>(),session,default);
        await unit.CommitAsync(default);
    });
}
