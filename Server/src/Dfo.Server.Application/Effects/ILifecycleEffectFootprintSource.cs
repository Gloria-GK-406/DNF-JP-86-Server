using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Effects;

/// <summary>Temporary explicit source for observer cleanup, removed by typed Internal migration.</summary>
public interface ILifecycleEffectFootprintSource
{
    ILifecycleEffectFootprintLease Open(InternalCommandOrigin origin);
}

public interface ILifecycleEffectFootprintLease : IDisposable
{
    IEffectFootprintRecorder Footprint { get; }
}
