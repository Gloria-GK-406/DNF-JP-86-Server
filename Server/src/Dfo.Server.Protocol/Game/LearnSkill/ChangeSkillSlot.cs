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

public sealed record ChangeSkillSlotRequest(GameMessageMetadata Metadata, byte Page, byte First, byte Second) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameChangeSkillSlot;
}
internal sealed class ChangeSkillSlotDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 11 && body[0] <= 1 && body[1] <= 203 && body[2] <= 203
            && LearnSkillMessageDecoder.HasValidChecksum(body[..7], body[7..])
            ? new ChangeSkillSlotRequest(metadata, body[0], body[1], body[2])
            : throw new InvalidDataException("A21 skill slot change requires an 11-byte body and valid page/slots.");
    // Bytes 3..6 remain opaque; bytes 7..10 use the A21 body checksum (verified against the captured frame).
}
public sealed record ChangeSkillSlotResponse(bool Success, byte Page, byte First, byte Second) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameChangeSkillSlot;
}
internal sealed class ChangeSkillSlotEncoder : GameCmdMessageEncoder<ChangeSkillSlotResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ChangeSkillSlotResponse message) =>
        OnePayload(0x001C, message.Success ? new byte[] { 1, message.Page, message.First, message.Second } : new byte[] { 0, 3 });
}
public sealed record ChangeSkillSlotCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, byte Page, byte First, byte Second)
    : ICommand<bool>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ChangeSkillSlotCommandHandler(ICharacterSkillLearningDomainService skills) : ICommandHandler<ChangeSkillSlotCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, ChangeSkillSlotCommand command, CancellationToken ct) =>
        skills.SwapSlotsAsync(command.Session, command.Page, command.First, command.Second, ct);
}
public sealed class ChangeSkillSlotHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, IGameMessageSender sender) : ClientMessageHandler<ChangeSkillSlotRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ChangeSkillSlotRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var success = false;
        if (session is not null && users.TryGetByConnection(context.Connection.ConnectionId, out var user) && user is not null)
            success = await commands.SendAsync(new ChangeSkillSlotCommand(user.OnlineUserId, session, message.Page, message.First, message.Second), ct);
        if (session is not null && !sessions.IsInWorld(session)) return;
        await sender.SendCmdAsync(context.Connection.ConnectionId, new ChangeSkillSlotResponse(success, message.Page, message.First, message.Second), ct);
    }
}
