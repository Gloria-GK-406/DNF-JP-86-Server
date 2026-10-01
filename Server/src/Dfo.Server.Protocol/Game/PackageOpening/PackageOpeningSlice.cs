using Dfo.Server.Abstractions.Effects;
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

namespace Dfo.Server.Protocol.Game.PackageOpening;

public sealed record PackageOpeningRequest(GameMessageMetadata Metadata, PackageOpeningInput? Input, AvatarPackageOpeningInput? AvatarInput = null) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GamePackageOpening;
}
internal sealed class PackageOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length == 2) return new PackageOpeningRequest(metadata, new(BinaryPrimitives.ReadInt16LittleEndian(body), []));
        // Selection UI writes slot, two opaque context bytes, selected IDs, then a counted avatar option list.
        PackageOpeningRequest? candidate = null;
        for (var avatars = 0; avatars <= 32; avatars++)
        {
            var selectedBytes = body.Length - 5 - 5 * avatars;
            if (selectedBytes < 4 || selectedBytes > 128 || selectedBytes % 4 != 0) continue;
            var countOffset = 4 + selectedBytes;
            if (body[countOffset] != avatars) continue;
            var selected = ImmutableArray.CreateBuilder<int>();
            for (var offset = 4; offset < countOffset; offset += 4) selected.Add(BinaryPrimitives.ReadInt32LittleEndian(body[offset..]));
            if (selected.Any(id => id <= 0) || selected.Distinct().Count() != selected.Count) continue;
            var options = ImmutableArray.CreateBuilder<AvatarPackageChoice>();
            for (var offset = countOffset + 1; offset < body.Length; offset += 5)
                options.Add(new(BinaryPrimitives.ReadInt32LittleEndian(body[offset..]), body[offset + 4]));
            if (avatars > 0 && (avatars != selected.Count || !options.Select(o => o.ItemId).SequenceEqual(selected))) continue;
            if (candidate is not null) return new PackageOpeningRequest(metadata, null); // ambiguous lengths are not a valid prefix
            var slot = BinaryPrimitives.ReadInt16LittleEndian(body);
            candidate = new(metadata, new(slot, selected.ToImmutable()), avatars == 0 ? null : new(slot, options.ToImmutable()));
        }
        return candidate ?? new PackageOpeningRequest(metadata, null);
    }
}
public sealed record PackageOpeningResponse(int SourceSlot, PackageOpeningResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePackageOpening;
}
internal sealed class PackageOpeningEncoder : GameCmdMessageEncoder<PackageOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PackageOpeningResponse message)
    {
        var body = message.Result.Receipt is { } receipt ? OpeningPayloads.Popup(message.SourceSlot, receipt.Rewards)
            : Convert.FromHexString("0000FFFFFFFFFFFFFFFF000000000000000000000000");
        return OnePayload(0x00A0, body);
    }
}
public sealed record PackageOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, PackageOpeningInput Input, AvatarPackageOpeningInput? AvatarInput = null)
    : ICommand<PackageOpeningResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class PackageOpeningCommandHandler(IPackageOpeningDomainService packages, IAvatarPackageOpeningDomainService avatars, TimeProvider clock)
    : ICommandHandler<PackageOpeningCommand, PackageOpeningResult>
{
    public ValueTask<PackageOpeningResult> HandleAsync(CommandExecutionContext context, PackageOpeningCommand command, CancellationToken ct) =>
        command.AvatarInput is { } options ? avatars.OpenAsync(command.Session, OpeningWireSlots.Business(options), clock.GetUtcNow(), ct) :
        packages.OpenAsync(command.Session, OpeningWireSlots.Business(command.Input), clock.GetUtcNow(), ct);
}
public sealed class PackageOpeningHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, INotificationBus notifications, IGameMessageSender sender, ILogger<PackageOpeningHandler> logger)
    : ClientMessageHandler<PackageOpeningRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PackageOpeningRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        PackageOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            try { result = await commands.SendAsync(new PackageOpeningCommand(online.OnlineUserId, session, message.Input, message.AvatarInput), ct).ConfigureAwait(false); }
            catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure)) { result = new(failure.Reason, Detail: failure.Message); }
            catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
            {
                logger.LogError(failure, "Package opening storage failure for {ConnectionId}", context.Connection.ConnectionId);
                result = new(OpeningRejection.StorageFailure);
            }
        }
        logger.LogInformation("PackageOpening connection={ConnectionId} slot={Slot} result={Result} detail={Detail}",
            context.Connection.ConnectionId, message.Input?.SourceSlot, result.Rejection, result.Detail);
        var response = new PackageOpeningResponse(message.Input?.SourceSlot ?? 0, result);
        if (result.Receipt is { } receipt)
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response]), ct).ConfigureAwait(false);
        else await sender.SendCmdAsync(context.Connection.ConnectionId, response, ct).ConfigureAwait(false);
    }
}
