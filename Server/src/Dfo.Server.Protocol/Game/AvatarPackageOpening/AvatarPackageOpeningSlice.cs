using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AvatarPackageOpening;

public sealed record AvatarPackageOpeningRequest(GameMessageMetadata Metadata, AvatarPackageOpeningInput? Input) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
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
        if (message.Result.Receipt is null) return OnePayload(message, new byte[] { 0, OpeningPayloads.Error(message.Result.Rejection) });
        var body = new byte[3]; body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), checked((short)message.SourceSlot));
        return OnePayload(message, body);
    }
}
public sealed class AvatarPackageOpeningHandler(ICommandBus commands) : ClientMessageHandler<AvatarPackageOpeningRequest>, IProtocolCommandMessageHandler<GameAvatarPackageOpeningCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AvatarPackageOpeningRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAvatarPackageOpeningCommand(context, message), ct).ConfigureAwait(false);
}
