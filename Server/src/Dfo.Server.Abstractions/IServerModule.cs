using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Abstractions;

/// <summary>
/// Defines a self-contained server module that contributes services to the host.
/// </summary>
public interface IServerModule
{
    /// <summary>
    /// Gets the stable module name used for diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers the services owned by this module.
    /// </summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
