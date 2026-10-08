using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record SetTitleAppearanceRequest(GameMessageMetadata Metadata, int ItemId, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetTitleAppearance;
}
internal sealed class SetTitleAppearanceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SetTitleAppearanceRequest(metadata, body.Length == 4 ? BinaryPrimitives.ReadInt32LittleEndian(body) : -1, body.Length == 4);
}
public sealed record SetTitleAppearanceCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int ItemId)
    : ICommand<bool>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class SetTitleAppearanceCommandHandler(ITitleBookDomainService titles) : ICommandHandler<SetTitleAppearanceCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, SetTitleAppearanceCommand command, CancellationToken ct) =>
        titles.SetAppearanceAsync(command.Session.CharacterId, command.ItemId, ct);
}
internal sealed record SetTitleAppearanceResponse(bool Success, int ItemId) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSetTitleAppearance;
    internal byte[] Body()
    {
        if (!Success) return [0, 1];
        var body = new byte[5]; body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), ItemId);
        return body;
    }
}
internal sealed class SetTitleAppearanceEncoder : GameCmdMessageEncoder<SetTitleAppearanceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SetTitleAppearanceResponse message) => OnePayload(0x0239, message.Body());
}
internal sealed class SetTitleAppearanceHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, IGameMessageSender sender, AchievementTitleProjectionGate gate) : ClientMessageHandler<SetTitleAppearanceRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetTitleAppearanceRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!message.Complete || message.ItemId < 0 || session is null || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null)
        { await sender.SendCmdAsync(context.Connection.ConnectionId, new SetTitleAppearanceResponse(false, 0), ct); return; }
        using var held = await gate.EnterAsync(session.CharacterId, ct).ConfigureAwait(false);
        var success = await commands.SendAsync(new SetTitleAppearanceCommand(user.OnlineUserId, session, message.ItemId), ct).ConfigureAwait(false);
        await sender.SendCmdAsync(session.ConnectionId, new SetTitleAppearanceResponse(success, Math.Max(0, message.ItemId)), ct).ConfigureAwait(false);
    }
}
