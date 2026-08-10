namespace Dfo.Server.Networking.Configuration;

public sealed class NetworkingOptions
{
    public const string SectionName = "Networking";

    public bool Enabled { get; init; }

    public int ListenPort { get; init; } = 7001;
}
