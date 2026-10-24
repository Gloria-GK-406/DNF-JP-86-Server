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
public sealed class PackageOpeningHandler(ICommandBus commands) : ClientMessageHandler<PackageOpeningRequest>, IProtocolCommandMessageHandler<GamePackageOpeningCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, PackageOpeningRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GamePackageOpeningCommand(context, message), ct).ConfigureAwait(false);
}
