using System.Collections.Immutable;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>Calls in a boss room entrance condition's monster once its targets are hunted.</summary>
/// <remarks>
/// The A21 client asks for the summon itself: NOTI 0x0138 makes the party leader's client read its
/// current map's <c>[conditional summon monster]</c> rows and send CMD 0x0211 (0x01193290), and the
/// answer's sequence is what it later reports the monster dead by. The server only decides whether
/// the summon is owed -- every target hunted, nothing summoned yet, the leader asking from a room
/// whose map declares the monster -- and adds the actor to that room so its death is accepted and
/// counted by the maze's <c>[hunt boss]</c> clear condition.
/// </remarks>
internal sealed class DungeonEntranceDomainService(
    IDungeonRunCombatDomainService runs,
    IDungeonSettlementRuleService settlementRules,
    IDungeonRoomRuleService rooms,
    IMonsterDropDomainService loot,
    ILogger<DungeonEntranceDomainService> logger) : IDungeonEntranceDomainService
{
    public async ValueTask<DungeonEntranceSummonResult> SummonAsync(SelectedSessionIdentity requester,
        DungeonEntranceSummonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(request);
        var run = runs.Find(requester.CharacterId);
        DungeonEntranceSummonResult Refused(string reason)
        {
            logger.LogInformation("Entrance summon refused character={CharacterId} run={RunId} code={Code} map={MapId} reason={Reason}",
                requester.CharacterId, run?.RunId, request.MonsterCode, request.MapId, reason);
            return new(request.RequestId, []);
        }
        if (run?.Layout.EntrancePlan is not { } plan) return Refused("NoEntrancePlan");
        if (!run.Participants.Any(member => member.CharacterId == requester.CharacterId && member.IsLeader))
            return Refused("NotLeader");
        if (request.Kind != 0 || request.Count == 0 || request.MonsterCode != plan.SummonCode
            || request.MapId != run.CurrentRoomMapId)
            return Refused("RequestMismatch");
        if (!settlementRules.IsEntranceConditionMet(plan,
                [.. run.Kills.Select(kill => new DungeonEntranceKill(kill.MonsterCode, kill.Actor.Cell))]))
            return Refused("TargetsOutstanding");
        // Once per run: a summoned monster is either still standing in this room or among the kills.
        if (run.Kills.Any(kill => kill.MonsterCode == plan.SummonCode)
            || run.CurrentCellState.Actors.Any(actor => actor.Actor.Code == plan.SummonCode))
            return Refused("AlreadySummoned");
        var templates = await rooms.DescribeConditionalSummonsAsync(run.Layout, run.CurrentRoomMapId,
            plan.SummonCode, cancellationToken).ConfigureAwait(false);
        if (templates.Count == 0) return Refused("MapDeclaresNoSummon");

        var count = Math.Min(Math.Min(request.Count, templates.Count), Math.Max(plan.SummonCount, 1));
        var used = run.CurrentCellState.Actors.Select(actor => actor.Sequence).ToHashSet();
        var next = (ushort)(used.Count == 0 ? run.CurrentCellState.FirstActorSequence : used.Max());
        var participants = run.Participants.Select(member => new MonsterDropParticipant(member.AccountId, member.CharacterId))
            .ToImmutableArray();
        var actors = new List<DungeonCellActorState>();
        foreach (var template in templates.Take(count))
        {
            do next = unchecked((ushort)(next + 1)); while (next == 0 || used.Contains(next));
            used.Add(next);
            var context = new MonsterDropContext(run.RunId, run.Layout.DungeonId, run.Layout.Seed, run.CurrentCell,
                run.CurrentRoomMapId, run.Difficulty, next, template.Level, template.ActorType, requester.CharacterId,
                participants) { RandomDropCount = template.RandomDropCount };
            var pending = new List<RolledDrop>(await loot.GenerateGroundAsync(template.Code, context, cancellationToken)
                .ConfigureAwait(false));
            foreach (var participant in participants)
                pending.AddRange(await loot.GeneratePersonalAsync(template.Code, participant.CharacterId, context,
                    cancellationToken).ConfigureAwait(false));
            actors.Add(new(next, template with { IsHostile = true }, IsAiCharacter: false, IsDead: false, pending));
        }
        if (!runs.StageDynamicCell(run.RunId, run.RoomVersion, run.CurrentCellState.Revision, actors,
                run.CurrentCellState.MechanismBlocksClear))
            return Refused("RoomChanged");
        logger.LogInformation("Entrance summon staged character={CharacterId} run={RunId} room={MapId} code={Code} sequences={Sequences}",
            requester.CharacterId, run.RunId, run.CurrentRoomMapId, plan.SummonCode,
            string.Join(",", actors.Select(actor => actor.Sequence)));
        return new(request.RequestId, [.. actors.Select(actor =>
            new DungeonEntranceSummoned(actor.Sequence, actor.Actor.Code, actor.Actor.ActorType, actor.Actor.Level))]);
    }
}
