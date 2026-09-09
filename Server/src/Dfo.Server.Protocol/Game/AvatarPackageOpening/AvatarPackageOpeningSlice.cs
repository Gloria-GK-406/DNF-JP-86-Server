using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AvatarPackageOpening;

public sealed record AvatarPackageOpeningRequest(GameMessageMetadata Metadata, AvatarPackageOpeningInput? Input) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameAvatarPackageOpening;
}
internal sealed class AvatarPackageOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < 3 || body[2] > 32 || body.Length != 3 + body[2] * 5) return new AvatarPackageOpeningRequest(metadata, null);
        var options = ImmutableArray.CreateBuilder<AvatarPackageChoice>();
        for (var offset = 3; offset < body.Length; offset += 5)
            options.Add(new(BinaryPrimitives.ReadInt32LittleEndian(body[offset..]), body[offset + 4]));
        return new AvatarPackageOpeningRequest(metadata, new(BinaryPrimitives.ReadInt16LittleEndian(body), options.ToImmutable()));
    }
}
public sealed record AvatarPackageOpeningResponse(int SourceSlot, PackageOpeningResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAvatarPackageOpening;
}
internal sealed class AvatarPackageOpeningEncoder : GameCmdMessageEncoder<AvatarPackageOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AvatarPackageOpeningResponse message)
    {
        if (message.Result.Receipt is null) return OnePayload(0x0207, new byte[] { 0, OpeningPayloads.Error(message.Result.Rejection) });
        var body = new byte[3]; body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), checked((short)message.SourceSlot));
        return OnePayload(0x0207, body);
    }
}
public sealed record AvatarPackageOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, AvatarPackageOpeningInput Input)
    : ICommand<PackageOpeningResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class AvatarPackageOpeningCommandHandler(IAvatarPackageOpeningDomainService avatars, IPackageOpeningDomainService packages, TimeProvider clock)
    : ICommandHandler<AvatarPackageOpeningCommand, PackageOpeningResult>
{
    public ValueTask<PackageOpeningResult> HandleAsync(CommandExecutionContext context, AvatarPackageOpeningCommand command, CancellationToken ct) =>
        command.Input.Choices.IsEmpty ? packages.OpenAsync(command.Session, new(command.Input.SourceSlot, []), clock.GetUtcNow(), ct)
            : avatars.OpenAsync(command.Session, command.Input, clock.GetUtcNow(), ct);
}
public sealed class AvatarPackageOpeningHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, INotificationBus notifications, IGameMessageSender sender, ILogger<AvatarPackageOpeningHandler> logger)
    : ClientMessageHandler<AvatarPackageOpeningRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AvatarPackageOpeningRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        PackageOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            try { result = await commands.SendAsync(new AvatarPackageOpeningCommand(online.OnlineUserId, session, message.Input), ct).ConfigureAwait(false); }
            catch (OpeningRejectedException failure) { result = new(failure.Reason, Detail: failure.Message); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger.LogError(failure, "Package opening storage failure for {ConnectionId}", context.Connection.ConnectionId);
                result = new(OpeningRejection.StorageFailure);
            }
        }
        logger.LogInformation("AvatarPackageOpening connection={ConnectionId} slot={Slot} result={Result} detail={Detail}",
            context.Connection.ConnectionId, message.Input?.SourceSlot, result.Rejection, result.Detail);
        var response = new AvatarPackageOpeningResponse(message.Input?.SourceSlot ?? 0, result);
        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response, new OpeningRewardPopup(message.Input!.SourceSlot, receipt.Rewards)]), ct).ConfigureAwait(false);
        else await sender.SendCmdAsync(context.Connection.ConnectionId, response, ct).ConfigureAwait(false);
    }
}
