using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.PvpColosseum;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PvpDailyMission;

public sealed class PvpDailyMissionProjection(IPvpDailyMissionQueryDomainService missions, IGameCommandOutbox outbox,
    IPvpColosseumDomainService colosseum)
{
    public async ValueTask ProjectAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        if (!channel.SupportsMatchmaking) return;
        var snapshot = await missions.CurrentAsync(session, channel, cancellationToken).ConfigureAwait(false);
        if (snapshot.Count > 0)
            await outbox.RegisterNotiAsync(session.ConnectionId, new PvpDailyMissionList(snapshot), cancellationToken)
                .ConfigureAwait(false);
        await new PvpColosseumProjection(colosseum, outbox).ProjectAsync(session, channel, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed record PvpDailyMissionList(IReadOnlyList<Abstractions.Pvp.PvpDailyMission> Missions) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpDailyMissionList;
}

internal sealed class PvpDailyMissionListEncoder : GameNotiMessageEncoder<PvpDailyMissionList>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpDailyMissionList message)
    {
        // With function 0x089F disabled, A21 reads exactly these three fields per mission.
        const int entryLength = sizeof(ushort) + 2 * sizeof(int);
        var body = new byte[1 + message.Missions.Count * entryLength];
        body[0] = checked((byte)message.Missions.Count);
        for (var index = 0; index < message.Missions.Count; index++)
        {
            var mission = message.Missions[index];
            var entry = body.AsSpan(1 + index * entryLength, entryLength);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, mission.Id);
            BinaryPrimitives.WriteInt32LittleEndian(entry[sizeof(ushort)..], mission.Progress);
            BinaryPrimitives.WriteInt32LittleEndian(entry[(sizeof(ushort) + sizeof(int))..], mission.Goal);
        }
        return OnePayload(message, body);
    }
}

internal static class PvpDailyMissionRegistration
{
    public static IServiceCollection AddPvpDailyMissionSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<PvpDailyMissionList, PvpDailyMissionListEncoder>();
        services.AddGameCmdMessageDecoder<PvpDailyHealthReportDecoder>(ClientMessageType.GamePvpMissionHpPercent);
        services.AddProtocolCommandHandler<GamePvpDailyHealthCommand, bool, PvpDailyHealthCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpMissionHpPercent, typeof(PvpDailyHealthReport)));
        services.RegisterProtocolMessageHandler<PvpDailyHealthReportHandler, PvpDailyHealthReport, GamePvpDailyHealthCommand>(
            ClientMessageType.GamePvpMissionHpPercent);
        services.AddGameCmdMessageDecoder<PvpDailyWinPoseReportDecoder>(ClientMessageType.GamePvpMissionWinPose);
        services.AddProtocolCommandHandler<GamePvpDailyWinPoseCommand, bool, PvpDailyWinPoseCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePvpMissionWinPose, typeof(PvpDailyWinPoseReport)));
        services.RegisterProtocolMessageHandler<PvpDailyWinPoseReportHandler, PvpDailyWinPoseReport, GamePvpDailyWinPoseCommand>(
            ClientMessageType.GamePvpMissionWinPose);
        services.AddProtocolCommandHandler<GamePvpDailySkillCommand, bool, PvpDailySkillCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameUseSkill, typeof(UseSkillReport)));
        services.RegisterProtocolMessageHandler<PvpDailySkillReportHandler, UseSkillReport, GamePvpDailySkillCommand>(
            ClientMessageType.GameUseSkill);
        return services;
    }
}
