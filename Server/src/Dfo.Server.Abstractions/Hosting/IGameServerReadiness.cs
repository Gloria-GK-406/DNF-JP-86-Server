namespace Dfo.Server.Abstractions.Hosting;

/// <summary>Whether this process can currently accept game sessions.</summary>
public interface IGameServerReadiness
{
    bool NetworkEnabled { get; }
    bool IsReady { get; }
}
