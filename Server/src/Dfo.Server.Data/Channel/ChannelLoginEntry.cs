using Dfo.Server.Data.Configuration;
namespace Dfo.Server.Data.Channel;

public sealed record ChannelLoginEntry(byte Environment, PublicGameLoginEndpoints Endpoints);
