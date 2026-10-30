using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TitleBook;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Achievements;

public sealed record AchievementTriggerRequest(GameMessageMetadata Metadata, int AchievementId,
    ushort Delta1, ushort Delta2, ushort Delta3, bool Complete) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameAchievementTrigger;
}
internal sealed class AchievementTriggerDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 10
        ? new AchievementTriggerRequest(metadata, BinaryPrimitives.ReadInt32LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[4..]), BinaryPrimitives.ReadUInt16LittleEndian(body[6..]), BinaryPrimitives.ReadUInt16LittleEndian(body[8..]), true)
        : new AchievementTriggerRequest(metadata, body.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(body) : -1, 0, 0, 0, false);
}
public sealed record AchievementTriggerResponse(int AchievementId, CharacterAchievementProgressState? Progress) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAchievementTrigger;
    internal byte[] Body()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Progress is null ? (byte)0 : (byte)1);
        if (Progress is null) writer.Write((byte)1);
        writer.Write(AchievementId);
        if (Progress is { } p) { writer.Write(p.P1); writer.Write(p.P2); writer.Write(p.P3); }
        return stream.ToArray();
    }
}
internal sealed class AchievementTriggerEncoder : GameCmdMessageEncoder<AchievementTriggerResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AchievementTriggerResponse message) => OnePayload(message, message.Body());
}
internal sealed class AchievementTriggerHandler(ICommandBus commands) : ClientMessageHandler<AchievementTriggerRequest>, IProtocolCommandMessageHandler<GameAchievementTriggerCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AchievementTriggerRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAchievementTriggerCommand(context, message), ct).ConfigureAwait(false);
}
internal sealed record AchievementChanged(CharacterId Character) : INotification<AchievementChanged>;
internal sealed class AchievementEventSubscriber(IEventService events) : IDomainEventSubscriber<AchievementProgressChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<AchievementProgressChanged> message, CancellationToken ct)
    {
        await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]), new AchievementChanged(message.Payload.CharacterId), ct).ConfigureAwait(false);
    }
}
internal sealed class AchievementChangedHandler(IAchievementDomainService achievements) : INotificationHandler<AchievementChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, AchievementChanged notification, CancellationToken ct) =>
        recipient.CharacterId != notification.Character ? [] : [new AchievementStateMessage(await achievements.GetAsync(notification.Character, ct).ConfigureAwait(false))];
}
