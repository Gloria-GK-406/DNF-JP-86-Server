using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Content an owner loads on first use and can load ahead of time. The game server's startup calls every
/// registered participant before it listens and off the gameplay executor, so no gameplay job pays for a
/// first load (spec RQ-091, appendix B "启动预热").
/// </summary>
/// <remarks>
/// A participant warms only what it already caches, through the same code a request would run; it adds no
/// cache of its own. A failure stays where the cache keeps it: a <see cref="Lazy{T}"/> rethrows to the
/// request that needs it, a first-use index retries, exactly as without the warmup.
/// </remarks>
public interface IContentWarmup
{
    string Name { get; }

    /// <summary>Loads everything this participant caches on first use; returns how many entries it holds.</summary>
    ValueTask<int> WarmAsync(CancellationToken cancellationToken);
}

public static class ContentWarmupRegistration
{
    /// <summary>
    /// Registers the singleton behind <typeparamref name="TService"/> as a warmup participant, so the warmup
    /// loads the very instance requests use. A double registered in its place is not a participant and
    /// warms nothing.
    /// </summary>
    public static IServiceCollection AddContentWarmup<TService>(this IServiceCollection services) where TService : class
    {
        services.AddSingleton<IContentWarmup>(provider =>
            provider.GetRequiredService<TService>() as IContentWarmup ?? new Replaced(typeof(TService).Name));
        return services;
    }

    /// <summary>True for the placeholder registered when a double replaced the participant's service.</summary>
    public static bool IsReplaced(IContentWarmup participant) => participant is Replaced;

    private sealed class Replaced(string service) : IContentWarmup
    {
        public string Name => service + " (replaced, not warmed)";
        public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }
}
