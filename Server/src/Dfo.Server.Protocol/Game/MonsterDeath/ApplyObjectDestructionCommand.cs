using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

public sealed record ApplyObjectDestructionCommand(
    SelectedSessionIdentity Session, DungeonRunId RunId, long RoomVersion, int ObjectCode,
    IReadOnlyList<SelectedSessionIdentity>? Recipients = null)
    : ICommand<ObjectDestructionResult>, IMultiAccountCommand
{
    public IReadOnlyList<SelectedSessionIdentity> Recipients { get; init; } =
        (Recipients ?? [Session]).ToImmutableArray();
    public IReadOnlyList<AccountId> AccountIds =>
        [Session.AccountId, .. this.Recipients.Select(recipient => recipient.AccountId)];
}

public sealed record ObjectQuestReward(SelectedSessionIdentity Session, ApplyMonsterKillResult Effects);
public sealed record ObjectDestructionResult(IReadOnlyList<ObjectQuestReward>? Rewards);

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
        // A reporting client is not the loot owner. Freeze exact sessions at the protocol
        // boundary, then revalidate them after all affected account leases have been acquired.
        if (!command.Recipients.Contains(session)
            || command.Recipients.Count != run.Participants.Count
            || command.Recipients.Select(value => value.CharacterId).Distinct().Count() != command.Recipients.Count
            || command.Recipients.Any(recipient => sessions.FindByConnection(recipient.ConnectionId) != recipient
                || runs.Find(recipient.CharacterId) is not { } memberRun
                || memberRun.RunId != command.RunId || memberRun.RoomVersion != command.RoomVersion
                || !run.Participants.Any(member => member.CharacterId == recipient.CharacterId
                    && member.AccountId == recipient.AccountId)))
            return new(null);
        var claim = runs.ClaimObjectDestruction(session.CharacterId, command.RunId,
            command.RoomVersion, command.ObjectCode);
        if (claim is null) return new(null);
        var rewards = new List<ObjectQuestReward>();
        foreach (var recipient in command.Recipients)
        {
            var granted = await PendingQuestItemGrants.GrantAsync(recipient.AccountId, recipient.CharacterId,
                claim.PendingLoot, default, questLogs, containers, items, null, ct, failIfFull: true)
                .ConfigureAwait(false);
            var updated = granted.HasChanges
                ? await progress.ReportAsync(recipient.AccountId, recipient.CharacterId,
                    [new QuestHoldingsChanged()], ct).ConfigureAwait(false)
                : null;
            rewards.Add(new(recipient, new ApplyMonsterKillResult(null, [], granted.CharacterItems,
                updated?.NotificationSnapshot, granted.AccountMaterials)));
        }
        return new(rewards);
    }
}
