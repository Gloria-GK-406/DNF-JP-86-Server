using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

public sealed record ApplyObjectDestructionCommand(
    SelectedSessionIdentity Session, DungeonRunId RunId, long RoomVersion, int ObjectCode)
    : ICommand<ObjectDestructionResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record ObjectDestructionResult(ApplyMonsterKillResult? Effects);

public sealed class ApplyObjectDestructionCommandHandler(
    IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions,
    ICharacterQuestLogDomainService questLogs,
    ICharacterContainerDomainService containers,
    IItemGrantDomainService items,
    IQuestProgressDomainService progress)
    : ICommandHandler<ApplyObjectDestructionCommand, ObjectDestructionResult>
{
    public async ValueTask<ObjectDestructionResult> HandleAsync(
        CommandExecutionContext context, ApplyObjectDestructionCommand command, CancellationToken ct)
    {
        if (context.ExecutionKind != CommandExecutionKind.Transactional)
            throw new InvalidOperationException("Object rewards require one transaction.");
        var session = command.Session;
        if (sessions.FindByConnection(session.ConnectionId) != session)
            return new(null);
        var run = runs.Find(session.CharacterId);
        if (run is null || run.RunId != command.RunId || run.RoomVersion != command.RoomVersion)
            return new(null);
        // Party reporter ownership is a separate acceptance boundary; do not spend a shared
        // queue while multiple clients may report the same destruction.
        if (run.PartyMemberCount != 1) return new(null);
        var claim = runs.ClaimObjectDestruction(session.CharacterId, command.RunId,
            command.RoomVersion, command.ObjectCode);
        if (claim is null) return new(null);
        var granted = await PendingQuestItemGrants.GrantAsync(session.AccountId, session.CharacterId,
            claim.PendingLoot, default, questLogs, containers, items, null, ct, failIfFull: true)
            .ConfigureAwait(false);
        var updated = granted.HasChanges
            ? await progress.ReportAsync(session.AccountId, session.CharacterId,
                [new QuestHoldingsChanged()], ct).ConfigureAwait(false)
            : null;
        return new(new ApplyMonsterKillResult(null, [], granted.CharacterItems,
            updated?.NotificationSnapshot, granted.AccountMaterials));
    }
}
