using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;
// Pre-existing domain fixtures drive the production whole body without nesting dispatcher jobs.
// This does not establish final scope disposal, default typed errors, or production inbound routing.
internal static class MapLoadingRouteDriver
{
    internal static async ValueTask RunAsync(IServiceProvider services, NetworkDispatchContext dispatch, IClientNetworkMessage input, CancellationToken token = default)
    {
        IProtocolCommand command = input switch
        {
            FinishLoadingRequest ready => new GameFinishLoadingCommand(dispatch, ready),
            MoveMapRequest move => new GameMoveMapCommand(dispatch, move),
            IllusionTowerStageRequest stage => new GameIllusionTowerStageCommand(dispatch, stage),
            _ => throw new NotSupportedException(),
        };
        var request = new CommandRequestContext.ProtocolNonEffect(dispatch, input, command.Frame, () => true);
        var context = DomainCommandFixture.Context(Guid.NewGuid(), command.GetType(), typeof(GameMapLoadingCommandHandler)) with { Request = request };
        var handler = ActivatorUtilities.CreateInstance<GameMapLoadingCommandHandler>(services);
        _ = command switch
        {
            GameFinishLoadingCommand ready => await handler.HandleAsync(context, ready, token),
            GameMoveMapCommand move => await handler.HandleAsync(context, move, token),
            GameIllusionTowerStageCommand stage => await handler.HandleAsync(context, stage, token),
            _ => false,
        };
        await request.DrainOperationsAsync(); request.Close();
    }
}
