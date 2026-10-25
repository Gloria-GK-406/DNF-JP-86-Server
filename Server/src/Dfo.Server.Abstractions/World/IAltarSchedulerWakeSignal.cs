namespace Dfo.Server.Abstractions.World;

/// <summary>Notifies the existing altar scheduler that its next deadline may have changed.</summary>
/// <remarks>Only a coalescing wake signal: no waiting, queue admission, world access or scheduling state.</remarks>
public interface IAltarSchedulerWakeSignal
{
    void Wake();
}
