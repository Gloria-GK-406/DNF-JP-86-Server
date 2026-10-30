using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed record SwitchSkillBuildRequest(GameMessageMetadata Metadata, byte Current) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSwitchSkillBuild;
}
internal sealed class SwitchSkillBuildDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1 && body[0] <= 1 ? new SwitchSkillBuildRequest(metadata, body[0])
        : throw new InvalidDataException("A21 skill build switch requires a current page 0 or 1.");
}
public sealed record SwitchSkillBuildResponse(int? Active) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSwitchSkillBuild;
}
internal sealed class SwitchSkillBuildEncoder : GameCmdMessageEncoder<SwitchSkillBuildResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SwitchSkillBuildResponse message) =>
        OnePayload(message, message.Active is { } active ? new byte[] { 1, checked((byte)active) } : new byte[] { 0, 3 });
}
public sealed class SwitchSkillBuildHandler(ICommandBus commands) : ClientMessageHandler<SwitchSkillBuildRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSwitchSkillBuildCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SwitchSkillBuildRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSwitchSkillBuildCommand(context,message),ct).ConfigureAwait(false);
}
