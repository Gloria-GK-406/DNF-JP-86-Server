namespace Dfo.Server.Abstractions.Configuration;

/// <summary>Provides required scalar server configuration values from an in-memory snapshot.</summary>
public interface IServerConfigService
{
    T GetConfig<T>(string key);
}
