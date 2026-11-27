using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildDonation;

public sealed record GuildDonationRequest(GameMessageMetadata Metadata, int Gold) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildDonateGold;
}

internal sealed class GuildDonationDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildDonationRequest(metadata, body.Length == sizeof(int) ? BinaryPrimitives.ReadInt32LittleEndian(body) : 0);
}

internal sealed record GuildDonationReply(byte? Error = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildDonateGold;
}

internal sealed class GuildDonationReplyEncoder : GameCmdMessageEncoder<GuildDonationReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDonationReply message) =>
        OnePayload(message, message.Error is { } error ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept([]));
}

public sealed class GuildDonationRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildDonationRequest>, IProtocolCommandMessageHandler<GameGuildDonationCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildDonationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildDonationCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildDonationRegistration
{
    internal static void AddGuildDonationSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildDonationDecoder>(ClientMessageType.GameGuildDonateGold);
        services.AddGameCmdMessageEncoder<GuildDonationReply, GuildDonationReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildDonationCommand, bool, GameGuildDonationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildDonateGold, typeof(GuildDonationRequest)));
        services.RegisterProtocolMessageHandler<GuildDonationRequestHandler, GuildDonationRequest, GameGuildDonationCommand>(
            ClientMessageType.GameGuildDonateGold);
    }
}
