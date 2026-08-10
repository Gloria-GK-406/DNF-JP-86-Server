namespace Dfo.Server.Networking.Configuration;

public sealed class NetworkingOptions
{
    public const string SectionName = "Networking";

    public bool Enabled { get; init; }

    public int ChannelPort { get; init; } = 7001;

    public IReadOnlyList<int> GamePorts { get; init; } = new[] { 10011 };
}
