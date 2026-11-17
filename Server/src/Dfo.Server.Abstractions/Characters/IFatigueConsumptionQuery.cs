namespace Dfo.Server.Abstractions.Characters;

/// <summary>The effective consumption policy; no source identity or activation state escapes this query.</summary>
public interface IFatigueConsumptionQuery
{
    ValueTask<bool> ShouldConsumeAsync(CancellationToken cancellationToken);
}
