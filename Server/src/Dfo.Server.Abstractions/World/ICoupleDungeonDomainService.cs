namespace Dfo.Server.Abstractions.World;

public interface ICoupleDungeonDomainService
{
    bool Supports(int dungeonId);
    ValueTask<bool> EligibleAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
