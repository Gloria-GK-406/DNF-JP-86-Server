using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DarkElfRuinsDomainService(IDarkElfRuinsRuleService rules, IDarkElfRuinsDomainStore store,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits,
    IDungeonRunCombatDomainService runs, ISelectedSessionDomainService sessions, ICharacterDomainService characters,
    IDailyResetRuleService daily, TimeProvider clock, IDomainTransactionAccessor transactions,
    IItemDeliveryDomainService delivery, IEventService events) : IDarkElfRuinsDomainService
{
    public DungeonRunKind Kind => DungeonRunKind.DarkElfRuins;
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    private DateOnly Day() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);

    private static DarkElfRuinsProgress At(DarkElfRuinsProgress value, DateOnly day)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return value with { Day = day, Entries = value.Day == day ? value.Entries : 0,
            Month = month, MonthlyEntries = value.Month == month ? value.MonthlyEntries : 0,
            Guardians = value.Month == month ? value.Guardians : 0 };
    }

    public async ValueTask<DarkElfRuinsSelection> SelectionAsync(CharacterId character, CancellationToken ct)
    {
        var day = Day();
        var progress = At(await store.ReadAsync(character, ct).ConfigureAwait(false), day);
        return new((byte)day.DayOfWeek, checked((byte)Math.Max(0, rules.Catalog.DailyEntries - progress.Entries)),
            progress.License, progress.Clears,
            rules.Catalog.Dungeons.Where(x => x.License == progress.License).OrderBy(x => x.Group).Select(x => x.DungeonId).ToArray());
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        Writable();
        if (!Supports(context.DungeonId) || context.RequestMode != 0 || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle
            || context.Participants.Count < 1 || context.Participants.Count > rules.MaximumParticipants)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Participants.Select(x => x.CharacterId).Distinct().Count() != context.Participants.Count
            || context.Participants.Select(x => x.AccountId).Distinct().Count() != context.Participants.Count)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var day = Day();
        DarkElfRuinsEntryRule rule;
        try { rule = await rules.EntryAsync(context.DungeonId, day.DayOfWeek, ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (!rule.Open) return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        if ((byte)context.Facts.Difficulty != rule.Difficulty)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        var expected = new Dictionary<CharacterId, DarkElfRuinsProgress>();
        var active = new Dictionary<CharacterId, DarkElfRuinsProgress>();
        var level = 1;
        foreach (var member in context.Participants)
        {
            var selected = sessions.Find(member.AccountId);
            if (selected?.CharacterId != member.CharacterId || !sessions.IsInWorld(selected))
                return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
            if (runs.Find(member.CharacterId) is not null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
            var raw = await store.ReadAsync(member.CharacterId, ct).ConfigureAwait(false);
            var state = At(raw, day);
            if (state.License < rule.Directory.License) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
            if (state.Entries >= rules.Catalog.DailyEntries) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DailyEntryLimitReached);
            expected.Add(member.CharacterId, raw); active.Add(member.CharacterId, state);
            level = Math.Max(level, (await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false))?.Level ?? 1);
        }
        var maze = rules.ChooseMaze(rule, active.Values.ToArray(), Random.Shared.Next());
        var guardian = maze.Guardian;
        var facts = context.Facts with { RequiredMazeIndex = maze.Index };
        DungeonLayout? layout;
        try { layout = await layouts.ResolveAsync(context.DungeonId, facts, Random.Shared.Next(), ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        layout = rules.PrepareLayout(layout, checked((byte)level), new(day, guardian, rule.Directory.License));
        var frozen = layout;
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            RequiredQuestIds = [rules.EntryQuestId], EnforceOrdinaryDifficulty = false,
        }, async (receipt, token) =>
        {
            foreach (var member in context.Participants)
            {
                var next = active[member.CharacterId];
                next = next with { Entries = checked(next.Entries + 1), MonthlyEntries = checked(next.MonthlyEntries + 1),
                    Guardians = checked(next.Guardians + (guardian ? 1 : 0)), Revision = checked(next.Revision + 1) };
                if (!await store.CompareExchangeAsync(member.CharacterId, expected[member.CharacterId], next, token).ConfigureAwait(false))
                    throw new InvalidOperationException("Ruins entry state changed after admission.");
            }
            return await visits.EnterGrantedPartyAsync(context.Participants, frozen, facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask CompleteClearAsync(DungeonActorDeathClaim death,
        IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken ct)
    {
        Writable();
        var run = runs.Find(death.CharacterId);
        if (run?.Layout.Mode is not DungeonRunMode.DarkElfRuins { Plan: var plan } || run.RunId != death.RunId || !run.IsBossRoom
            || !runs.IsActorDeathClaimCurrent(death) || !runs.WillClearMap(death))
            throw new InvalidOperationException("Ruins settlement requires an authoritative boss-room clear claim.");
        if (participants.Count != run.Participants.Count || participants.Count == 0
            || participants.Select(x => x.CharacterId).Distinct().Count() != participants.Count
            || participants.Any(x => !sessions.IsInWorld(x) || runs.Find(x.CharacterId)?.RunId != run.RunId))
            throw new InvalidOperationException("Ruins settlement participants changed.");
        var rule = await rules.EntryAsync(run.Layout.DungeonId, plan.Day.DayOfWeek, ct).ConfigureAwait(false);
        // Guardian rewards require the actual guardian death, never merely selecting its maze.
        var guardianDefeated = plan.Guardian && (run.Kills.Any(x => rules.IsGuardian(x.MonsterCode))
            || death.ActorState is { } actor && rules.IsGuardian(actor.Actor.Code));
        foreach (var session in participants)
        {
            var frozen = rules.FreezeReward(rule, guardianDefeated,
                (int)Math.Clamp((clock.GetUtcNow() - run.StartedAt).TotalMilliseconds, 0, int.MaxValue), Random.Shared.Next());
            if (!await store.FreezeRewardAsync(session.CharacterId, run.RunId.Value, frozen, ct).ConfigureAwait(false)) continue;
            var old = await store.ReadAsync(session.CharacterId, ct).ConfigureAwait(false);
            var next = rules.Advance(old, plan.License, run.RevivalCounts.GetValueOrDefault(session.CharacterId) > 0
                || run.RevivalCoinPayments.GetValueOrDefault(session.CharacterId) > 0);
            next = next with { Revision = checked(old.Revision + 1) };
            if (!await store.CompareExchangeAsync(session.CharacterId, old, next, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Ruins progression changed during clear settlement.");
        }
    }

    public async ValueTask<DarkElfRuinsReward?> ResultAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writable();
        var run = CurrentClear(session);
        if (run is null) return null;
        await store.AdvancePresentationAsync(session.CharacterId, run.RunId.Value, ct).ConfigureAwait(false);
        return await store.RewardAsync(session.CharacterId, run.RunId.Value, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> ClaimAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writable();
        var run = CurrentClear(session);
        return run is not null && (await store.RewardAsync(session.CharacterId, run.RunId.Value, ct).ConfigureAwait(false)) is { PresentationStage: >= 2 }
            && await DeliverAsync(session, run.RunId.Value, ct).ConfigureAwait(false);
    }

    public async ValueTask RecoverRewardsAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        Writable();
        if (!sessions.IsInWorld(session)) return;
        foreach (var id in await store.PendingRewardsAsync(session.CharacterId, ct).ConfigureAwait(false))
            await DeliverAsync(session, id, ct).ConfigureAwait(false);
    }

    private async ValueTask<bool> DeliverAsync(SelectedSessionIdentity session, Guid id, CancellationToken ct)
    {
        var reward = await store.RewardAsync(session.CharacterId, id, ct).ConfigureAwait(false);
        if (reward is null) return false;
        if (reward.Claimed) return true;
        if (!await store.ClaimRewardAsync(session.CharacterId, id, ct).ConfigureAwait(false)) return false;
        try
        {
            var receipt = await delivery.DeliverAsync(session.AccountId, session.CharacterId,
                reward.Clear.Concat([reward.Daily]).Concat(reward.Guardian).ToArray(), "暗精灵遗迹通关奖励", ct,
                ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            await events.EnqueueAsync(new DarkElfRuinsInventoryChanged(session.CharacterId,
                receipt.Inventory.Select(x => x.Slot).Distinct().ToArray()), ct).ConfigureAwait(false);
            return true;
        }
        catch { transactions.Current!.RequireRollback(); throw; }
    }

    public bool CanRevive(CharacterId character, DungeonRunId id) => runs.Find(character) is { Layout.Mode: DungeonRunMode.DarkElfRuins } run
        && run.RunId == id && run.RevivalCoinPayments.GetValueOrDefault(character) < rules.ReviveLimit;
    private DungeonRunState? CurrentClear(SelectedSessionIdentity session) => sessions.IsInWorld(session)
        && runs.Find(session.CharacterId) is { Layout.Mode: DungeonRunMode.DarkElfRuins, ClearOpen: true } run ? run : null;
    private void Writable()
    {
        if (transactions.Current is not { IsReadOnly: false }) throw new InvalidOperationException("Ruins mutations require a writable transaction.");
    }
}
