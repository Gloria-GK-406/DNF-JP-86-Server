using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

public interface IChannelTcpService : ITcpService;

public sealed class ChannelTcpService : TcpService, IChannelTcpService;

public interface IGameTcpService : ITcpService;

public sealed class GameTcpService : TcpService, IGameTcpService;
