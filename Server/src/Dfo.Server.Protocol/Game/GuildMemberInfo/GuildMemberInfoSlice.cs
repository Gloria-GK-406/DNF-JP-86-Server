using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildMemberInfo;

internal enum GuildRank : byte
{
    NoGuild = 0,
    Leader = GuildRankSettings.Leader,
    ViceLeader = GuildRankSettings.ViceLeader,
    SeniorMember = 3,
    Member = GuildRankSettings.Member,
    EliteMember = GuildRankSettings.LastRank,
}

internal sealed record GuildMemberInfoMessage(GuildRank Rank) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildMemberInfo;

    internal static GuildMemberInfoMessage From(GuildMember? member) => new(RankOf(member));

    internal static GuildRank RankOf(GuildMember? member) => member is null ? GuildRank.NoGuild : (GuildRank)member.Rank;
}

internal sealed class GuildMemberInfoEncoder : GameNotiMessageEncoder<GuildMemberInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildMemberInfoMessage message) =>
        OnePayload(message, new byte[] { (byte)message.Rank });
}

internal static class GuildMemberInfoRegistration
{
    internal static void AddGuildMemberInfoSlice(this IServiceCollection services) =>
        services.AddGameNotiMessageEncoder<GuildMemberInfoMessage, GuildMemberInfoEncoder>();
}
