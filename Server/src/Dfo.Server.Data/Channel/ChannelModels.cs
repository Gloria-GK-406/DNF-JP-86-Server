namespace Dfo.Server.Data.Channel;

public sealed record ChannelServerInfo(
    int ChannelId,
    string Name,
    int MaximumUsers,
    int CurrentUsers,
    string Host,
    int Port);
