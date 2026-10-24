using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>What came of paying for a finished run.</summary>
/// <param name="Award">
/// What was awarded and where the character now stands, or nothing when there was no character to
/// award to. The same distinction a kill draws: an award of nothing still knows the character's
/// standing, and this does not.
/// </param>
/// <param name="ClearBase">What the clear itself was worth, for the result screen to report.</param>
/// <param name="RankBonus">What the run's rank added, likewise.</param>
public sealed record ClearExperienceResult(
    ExperienceAward? Award, uint ClearBase, uint RankBonus, bool AttendanceChanged = false,
    CharacterDungeonDifficulty? DifficultyUnlocked = null)
{
    public DespairTowerSettlement? Despair { get; init; }
}

/// <summary>One character's finished run, paid.</summary>
public sealed record GrantClearExperienceCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    int DungeonId,
    ClearCircumstances Clear, SelectedSessionIdentity? Session = null,
    Guid RunId = default,int SettlementLevel = 0,DateTimeOffset? SettledAt = null) : ICommand<ClearExperienceResult>, ISelectedSessionCommand,IAccountCommand;

/// <summary>Calculate settlement and persist progression within one transaction.</summary>
public sealed class GrantClearExperienceCommandHandler(
    ICharacterDomainService grant, TimeProvider clock, ICharacterDungeonDifficultyDomainService difficulties,
    Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService? attendance = null,
    IQuestProgressDomainService? quests = null, IDungeonRunDomainService? runs = null, IDespairTowerDomainService? despair = null,
    Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService? luckyStars = null)
    : ICommandHandler<GrantClearExperienceCommand, ClearExperienceResult>
{
    public async ValueTask<ClearExperienceResult> HandleAsync(CommandExecutionContext context,
        GrantClearExperienceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ExecutionKind is not CommandExecutionKind.Transactional)
            throw new InvalidOperationException("Awarding experience requires a transaction.");
        var now = command.SettledAt ?? clock.GetUtcNow();
        DespairTowerSettlement? tower = null;
        if (runs?.Find(command.CharacterId) is { Layout.Despair: not null } towerRun)
        {
            tower = await (despair ?? throw new InvalidOperationException("Despair settlement is unavailable."))
                .SettleAsync(command.AccountId, command.CharacterId, new(command.RunId), cancellationToken).ConfigureAwait(false);
            if (tower is null) return new(null, 0, 0);
        }
        if (quests is not null && runs?.Find(command.CharacterId) is { Layout.Mirror: not null } mirror
            && mirror.RunId.Value == command.RunId && mirror.ClearOpen)
            await quests.ReportAsync(command.AccountId, command.CharacterId,
                [new QuestDungeonCleared(command.DungeonId, mirror.Difficulty, mirror.RunId, mirror.ClearedAt ?? now)], cancellationToken).ConfigureAwait(false);
        if (luckyStars is not null && command.RunId != Guid.Empty)
            await luckyStars.GrantForClearAsync(command.AccountId, command.CharacterId, new(command.RunId), cancellationToken).ConfigureAwait(false);
        var award = await grant.Get(command.CharacterId).AddExpAsync(new ClearExperienceContext(
                new ExperienceOccasion(ExperienceEvent.DungeonClear, command.AccountId,
                    command.CharacterId, command.DungeonId), command.Clear), now, command.Session,
            context.LegacyFootprint, cancellationToken).ConfigureAwait(false);
        var changed = award is not null && attendance is not null && command.RunId != Guid.Empty
            && await attendance.RecordClearAsync(command.AccountId,command.RunId,command.DungeonId,command.SettlementLevel,now,cancellationToken);
        var unlocked = runs?.Find(command.CharacterId)?.Layout is { Mirror: not null } or { Despair: not null } ? null
            : await difficulties.UnlockAfterClearAsync(command.CharacterId, command.DungeonId,
            (DungeonDifficulty)command.Clear.Difficulty, cancellationToken).ConfigureAwait(false);
        return new(award, award?.Breakdown.Base ?? 0, award?.Breakdown.ClearRank ?? 0,changed,unlocked) { Despair = tower };
    }
}
