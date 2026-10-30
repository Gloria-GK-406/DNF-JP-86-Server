using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal interface IAltarModeDomainService : IAltarDungeonDomainService
{
    void Remove(DungeonRunId runId);
}

internal sealed class EndlessAltarDomainService(AltarDungeonEngine engine, IDungeonDeadlineWake deadlines) : IAltarModeDomainService
{
    private readonly AltarStateStore state = new(deadlines);
    public bool Supports(int dungeonId) => dungeonId == 11006;
    public AltarDungeonState? Find(DungeonRunId runId) => engine.Find(state, runId);
    public IReadOnlyList<AltarDeadline> Pending() => engine.Pending(state);
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => engine.EnterAsync(state, 11006, context, ct);
    public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, CancellationToken ct) => engine.ReadyAsync(state, session, runId, roomVersion, ct);
    public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId runId, long roomVersion, long generation, DateTimeOffset now, CancellationToken ct) => engine.AdvanceAsync(state, runId, roomVersion, generation, now, ct);
    public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, long generation, byte difficulty, CancellationToken ct) => engine.SelectDifficultyAsync(state, session, runId, roomVersion, generation, difficulty, ct);
    public ValueTask<AltarActionResult?> FailAsync(DungeonRunId runId, CancellationToken ct) => engine.FailAsync(state, runId, ct);
    public ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId runId, CancellationToken ct, CharacterId? participant = null) => engine.SettleBeforeExitAsync(state, runId, ct, participant);
    public async ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct) => [await engine.ReadQuotaAsync(11006, characterId, ct).ConfigureAwait(false)];
    public bool CanRevive(CharacterId characterId, DungeonRunId runId) => engine.CanRevive(state, characterId, runId);
    public bool StageRevival(CharacterId characterId, DungeonRunId runId) => engine.StageRevival(state, characterId, runId);
    public void Remove(DungeonRunId runId) => state.Remove(runId);
}

internal sealed class UltimateAltarDomainService(AltarDungeonEngine engine, IDungeonDeadlineWake deadlines) : IAltarModeDomainService
{
    private readonly AltarStateStore state = new(deadlines);
    public bool Supports(int dungeonId) => dungeonId == 11007;
    public AltarDungeonState? Find(DungeonRunId runId) => engine.Find(state, runId);
    public IReadOnlyList<AltarDeadline> Pending() => engine.Pending(state);
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => engine.EnterAsync(state, 11007, context, ct);
    public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, CancellationToken ct) => engine.ReadyAsync(state, session, runId, roomVersion, ct);
    public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId runId, long roomVersion, long generation, DateTimeOffset now, CancellationToken ct) => engine.AdvanceAsync(state, runId, roomVersion, generation, now, ct);
    public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, long generation, byte difficulty, CancellationToken ct) => engine.SelectDifficultyAsync(state, session, runId, roomVersion, generation, difficulty, ct);
    public ValueTask<AltarActionResult?> FailAsync(DungeonRunId runId, CancellationToken ct) => engine.FailAsync(state, runId, ct);
    public ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId runId, CancellationToken ct, CharacterId? participant = null) => engine.SettleBeforeExitAsync(state, runId, ct, participant);
    public async ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct) => [await engine.ReadQuotaAsync(11007, characterId, ct).ConfigureAwait(false)];
    public bool CanRevive(CharacterId characterId, DungeonRunId runId) => engine.CanRevive(state, characterId, runId);
    public bool StageRevival(CharacterId characterId, DungeonRunId runId) => engine.StageRevival(state, characterId, runId);
    public void Remove(DungeonRunId runId) => state.Remove(runId);
}

/// <summary>Transport-neutral routing facade; contains no gameplay state.</summary>
internal sealed class AltarDungeonDomainService(EndlessAltarDomainService endless, UltimateAltarDomainService ultimate,
    IDungeonRunQueryDomainService runs) : IAltarDungeonDomainService,
    IDomainEventSubscriber<DungeonParticipantLeft>
{
    public bool Supports(int dungeonId) => dungeonId is 11006 or 11007;
    private IAltarModeDomainService? Store(DungeonRunId runId) => (runs.Find(runId)?.Layout.Mode as DungeonRunMode.Altar)?.Plan.Kind switch
    {
        AltarDungeonKind.Endless => endless,
        AltarDungeonKind.Ultimate => ultimate,
        _ => null,
    };
    public AltarDungeonState? Find(DungeonRunId runId) => Store(runId) is { } store ? store.Find(runId) : null;
    public IReadOnlyList<AltarDeadline> Pending() => endless.Pending().Concat(ultimate.Pending()).ToArray();
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => context.DungeonId switch
    {
        11006 => endless.EnterAsync(context, ct),
        11007 => ultimate.EnterAsync(context, ct),
        _ => ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)),
    };
    public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion, CancellationToken ct) =>
        Store(runId) is { } store ? store.ReadyAsync(session, runId, roomVersion, ct) : ValueTask.FromResult<AltarActionResult?>(null);
    public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId runId, long roomVersion, long generation, DateTimeOffset now, CancellationToken ct) =>
        Store(runId) is { } store ? store.AdvanceAsync(runId, roomVersion, generation, now, ct) : ValueTask.FromResult<AltarActionResult?>(null);
    public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId runId, long roomVersion,
        long generation, byte difficulty, CancellationToken ct) => Store(runId) is { } store
            ? store.SelectDifficultyAsync(session, runId, roomVersion, generation, difficulty, ct) : ValueTask.FromResult<AltarActionResult?>(null);
    public ValueTask<AltarActionResult?> FailAsync(DungeonRunId runId, CancellationToken ct) => Store(runId) is { } store
        ? store.FailAsync(runId, ct) : ValueTask.FromResult<AltarActionResult?>(null);
    public ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId runId, CancellationToken ct, CharacterId? participant = null) => Store(runId) is { } store
        ? store.SettleBeforeExitAsync(runId, ct, participant) : ValueTask.FromResult<AltarActionResult?>(null);
    public async ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId characterId, CancellationToken ct) =>
        (await endless.ReadQuotaAsync(characterId, ct).ConfigureAwait(false)).Concat(await ultimate.ReadQuotaAsync(characterId, ct).ConfigureAwait(false)).ToArray();
    public bool CanRevive(CharacterId characterId, DungeonRunId runId) => Store(runId) is { } store && store.CanRevive(characterId, runId);
    public bool StageRevival(CharacterId characterId, DungeonRunId runId) => Store(runId) is { } store && store.StageRevival(characterId, runId);
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonParticipantLeft> message, CancellationToken ct)
    {
        if (runs.Find(message.Payload.RunId) is null)
        {
            endless.Remove(message.Payload.RunId);
            ultimate.Remove(message.Payload.RunId);
        }
        return ValueTask.CompletedTask;
    }
}
