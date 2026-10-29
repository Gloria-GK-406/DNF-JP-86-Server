namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Chooses the gameplay that admits an ordinary-engine entry and enters through it.
/// </summary>
/// <remarks>
/// The order is the routing rule: a dungeon a dedicated gameplay recognises never falls through to
/// mirror, cycle or the hell/normal decision, whatever the request carries. The two towers are
/// separate engines and are chosen before this is consulted.
/// </remarks>
public interface IDungeonEntryRoutingDomainService
{
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext entry, CancellationToken ct);
}
