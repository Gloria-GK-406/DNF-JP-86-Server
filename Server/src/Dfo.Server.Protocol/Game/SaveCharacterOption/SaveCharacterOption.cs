using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.SaveCharacterOption;

public sealed record SaveCharacterOptionRequest(GameMessageMetadata Metadata, byte[] Body) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSaveCharacterOption;
}
internal sealed class SaveCharacterOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 516 && BinaryPrimitives.ReadUInt32LittleEndian(body) == 512
            ? new SaveCharacterOptionRequest(metadata,body.ToArray())
            : throw new InvalidDataException("A21 character options require u32(512) and 512 bytes.");
}
public sealed record SaveCharacterOptionCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, byte[] Body)
    : ICommand<bool>, IOnlineUserCommand, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class SaveCharacterOptionCommandHandler(ICharacterOptionsDomainService options) : ICommandHandler<SaveCharacterOptionCommand,bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context,SaveCharacterOptionCommand command,CancellationToken ct) =>
        options.SaveAsync(command.Session,command.Body,ct);
}
public sealed class SaveCharacterOptionHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICommandBus commands, ILogger<SaveCharacterOptionHandler> logger) : ClientMessageHandler<SaveCharacterOptionRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,SaveCharacterOptionRequest message,CancellationToken ct)
    {
        var session=sessions.FindByConnection(context.Connection.ConnectionId);
        if(session is null || !users.TryGetByConnection(context.Connection.ConnectionId,out var user) || user is null) return;
        var saved=await commands.SendAsync(new SaveCharacterOptionCommand(user.OnlineUserId,session,message.Body),ct);
        logger.LogInformation("Character options character={CharacterId} bytes={Bytes} saved={Saved}",session.CharacterId.Value,message.Body.Length,saved);
        // No CMD ACK: the client has no response handler for this upload.
    }
}
