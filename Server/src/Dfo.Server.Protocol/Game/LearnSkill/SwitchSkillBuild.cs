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
        OnePayload(0x0104, message.Active is { } active ? new byte[] { 1, checked((byte)active) } : new byte[] { 0, 3 });
}
public sealed record SwitchSkillBuildCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, byte Current)
    : ICommand<SwitchSkillBuildResponse>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class SwitchSkillBuildCommandHandler(ICharacterSkillLearningDomainService learning)
    : ICommandHandler<SwitchSkillBuildCommand, SwitchSkillBuildResponse>
{
    public async ValueTask<SwitchSkillBuildResponse> HandleAsync(CommandExecutionContext context, SwitchSkillBuildCommand command, CancellationToken ct) =>
        new(await learning.SwitchAsync(command.Session, command.Current, 1 - command.Current, ct));
}
public sealed class SwitchSkillBuildHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICommandBus commands, IGameMessageSender sender)
    : ClientMessageHandler<SwitchSkillBuildRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SwitchSkillBuildRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        int? active = null;
        if (session is not null && users.TryGetByConnection(context.Connection.ConnectionId, out var user) && user is not null)
            active = (await commands.SendAsync(new SwitchSkillBuildCommand(user.OnlineUserId, session, message.Current), ct)).Active;
        if (session is not null && sessions.FindByConnection(context.Connection.ConnectionId) != session) return;
        await sender.SendCmdAsync(context.Connection.ConnectionId, new SwitchSkillBuildResponse(active), ct);
    }
}
