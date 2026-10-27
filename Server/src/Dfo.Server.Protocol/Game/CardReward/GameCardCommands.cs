using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
namespace Dfo.Server.Protocol.Game.CardReward;
public sealed record GameScoreScrollStateCommand(NetworkDispatchContext Dispatch, ScoreScrollStateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameCardSelectRightStateCommand(NetworkDispatchContext Dispatch, CardSelectRightStateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameSelectCardCommand(NetworkDispatchContext Dispatch, SelectCardRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record DungeonCardLayoutCommand(SelectedSessionIdentity Session, DungeonRunId RunId, CancellationToken PendingCancellation) : ICommand<bool>;
public sealed record DungeonCardAutoFlipCommand(SelectedSessionIdentity Session, DungeonRunId RunId, CancellationToken PendingCancellation) : ICommand<bool>;
