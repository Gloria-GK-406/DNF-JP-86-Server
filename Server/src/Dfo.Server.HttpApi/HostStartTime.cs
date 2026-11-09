namespace Dfo.Server.HttpApi;

/// <summary>
/// When this Host started, as the gate status reports it. <see cref="HttpApiStartupService"/> takes it
/// as a dependency, so it is captured while the Host starts rather than on the first status request.
/// </summary>
internal sealed class HostStartTime(DateTimeOffset value)
{
    public DateTimeOffset Value { get; } = value;
}
