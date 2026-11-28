using System.Buffers.Binary;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.LuckyCharacter;

internal sealed record LuckyCharacterListMessage(LuckyCharacterPeriod Period)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    private const int GrowthTypes = 5;
    private const int AwakeningStages = 3;
    internal override ushort Type => GameProtocol.LuckyCharacterListType;

    internal override byte[] EncodeBody()
    {
        var count = Period.Jobs.Length * GrowthTypes * AwakeningStages;
        var body = new byte[sizeof(int) + count * 2 * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(body, count);
        var offset = sizeof(int);
        foreach (var job in Period.Jobs)
        {
            for (var awakening = 0; awakening < AwakeningStages; awakening++)
            {
                for (var growth = 0; growth < GrowthTypes; growth++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), job);
                    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + sizeof(int)), (awakening << 4) | growth);
                    offset += 2 * sizeof(int);
                }
            }
        }
        return body;
    }
}

public sealed class LuckyCharacterRosterDelivery(ILuckyCharacterEventQuery events, TimeProvider clock,
    IChannelEntryDomainService channels,
    IGameCommandOutbox outbox)
{
    public async ValueTask RegisterAsync(NetworkConnectionContext connection, CancellationToken ct)
    {
        var period = await events.ReadAtAsync(clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (period.Revision <= 0) return;
        var channel = await channels.ResolveAsync(connection.ListenerPort, ct).ConfigureAwait(false);
        if (channel.IsPvp)
            period = period with { Jobs = [] };
        await outbox.RegisterNotiAsync(connection.ConnectionId, new LuckyCharacterListMessage(period), ct).ConfigureAwait(false);
    }
}
