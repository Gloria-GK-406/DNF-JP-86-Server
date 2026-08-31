using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// What a character's inventory is granted on the way in.
/// </summary>
/// <remarks>
/// The daily refill is content plus one fact: the archive declares what is refilled, how much of it
/// and until when, and the moment the entry was prepared decides which of those declarations are
/// still live. The moment is handed in, so nothing here reads a clock of its own.
/// </remarks>
public interface IInventoryLifecycleRuleService
{
    /// <summary>What this entry's daily refill grants, at the moment it was prepared.</summary>
    ValueTask<IReadOnlyList<DailyRefillRule>> DailyRefillsAsync(
        DateTimeOffset preparedAt, CancellationToken cancellationToken);
}
