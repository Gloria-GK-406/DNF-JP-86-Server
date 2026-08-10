using System.Text;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Networking.Configuration;

public sealed class GameLoginOptions
{
    public const string SectionName = "GameLogin";

    public string ServerIp { get; init; } = "127.0.0.1";

    public byte Environment { get; init; } = 0x01;

    public int LoginChannelPort { get; init; } = 10128;

    public int UnknownPort { get; init; } = 17200;

    internal static GameLoginOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new GameLoginOptions
        {
            ServerIp = section[nameof(ServerIp)] ?? "127.0.0.1",
            Environment = byte.TryParse(section[nameof(Environment)], out var environment)
                ? environment
                : (byte)0x01,
            LoginChannelPort = int.TryParse(section[nameof(LoginChannelPort)], out var channelPort)
                ? channelPort
                : 10128,
            UnknownPort = int.TryParse(section[nameof(UnknownPort)], out var unknownPort)
                ? unknownPort
                : 17200,
        };
        if (Encoding.ASCII.GetByteCount(options.ServerIp) > 256)
        {
            throw new InvalidOperationException("GameLogin:ServerIp cannot exceed 256 ASCII bytes.");
        }

        ValidatePort(options.LoginChannelPort, nameof(LoginChannelPort));
        ValidatePort(options.UnknownPort, nameof(UnknownPort));
        return options;
    }

    private static void ValidatePort(int port, string name)
    {
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException($"GameLogin:{name} must be a valid TCP port.");
        }
    }
}
