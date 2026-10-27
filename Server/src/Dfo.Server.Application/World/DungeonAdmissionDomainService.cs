using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonAdmissionDomainService(IDungeonStructureRuleService structures,
    ICharacterDungeonDifficultyDomainService difficulties, IDungeonDifficultyRuleService difficultyRules,
    ICharacterFatigueDomainService fatigue, IDungeonEntryMaterialsDomainService? materials = null,
    ICharacterDomainService? characters = null, IIllusionTowerDomainService? tower = null,
    ICharacterQuestLogDomainService? quests = null, IUsageQuotaDomainService? quotas = null,
    IEventService? events = null, IDomainTransactionAccessor? transactions = null,
    ISelectedSessionDomainService? sessions = null, IOnlineCharacterRuntimeDomainService? onlineCharacters = null,
    IDungeonRunDomainService? runs = null, CoupleDungeonRule? couples = null, IDungeonAdmissionRuleService? admissionRules = null, IDungeonParticipationDomainService? participation = null) : IDungeonAdmissionDomainService
{
    public async ValueTask<DungeonAdmissionDecision> PrepareAsync(DungeonEntryContext context, DungeonAdmissionOptions options, CancellationToken ct)
    {
        if (context.Participants.Count == 0 || context.Participants.Select(x => x.CharacterId).Distinct().Count() != context.Participants.Count
            || context.Participants.Select(x => x.AccountId).Distinct().Count() != context.Participants.Count
            || context.Participants.Select(x => x.Slot).Distinct().Count() != context.Participants.Count)
            return Refuse(DungeonEntryFailure.MissingPrerequisite);
        if (context.DungeonId == CoupleDungeonRule.DungeonId && (couples is null || !await couples.EligibleAsync(context, ct).ConfigureAwait(false)))
            return Refuse(DungeonEntryFailure.MissingPrerequisite);
        if (options.Quota is { Family: not (UsageQuotaFamily.MirrorDungeon or UsageQuotaFamily.DimensionDungeon or UsageQuotaFamily.DespairTower or UsageQuotaFamily.AltarDungeon or UsageQuotaFamily.TournamentDungeon or UsageQuotaFamily.HerasDungeon or UsageQuotaFamily.GoldSilverHorn) })
            throw new ArgumentException("Unsupported dungeon entry quota family.", nameof(options));
        if (!Enum.IsDefined(context.Facts.Difficulty)) return Refuse(DungeonEntryFailure.DifficultyNotUnlocked);
        DungeonAdmissionDefinition? definition;
        try
        {
            definition = admissionRules is not null
                ? await admissionRules.GetAsync(context.DungeonId, ct).ConfigureAwait(false)
                : await structures.GetDefinitionAsync(context.DungeonId, ct).ConfigureAwait(false) is { } ordinary
                    ? DungeonAdmissionDefinition.From(ordinary) : null;
        }
        catch (InvalidDataException error) { return new(null, DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message)); }
        if (options.RequireMinimumLevel && definition is null) return Refuse(DungeonEntryFailure.DungeonNotFound);
        var permits = ImmutableArray.CreateBuilder<IDungeonAdmissionPermit>();
        var eligible = new List<(DungeonRunParticipant Member, SelectedSessionIdentity? Selected)>();
        foreach (var member in context.Participants)
        {
            if (participation?.Find(member.CharacterId) is not null || tower?.IsInTower(member.CharacterId) == true || options.MirrorCatalog is not null && runs?.Find(member.CharacterId) is not null)
                return Refuse(DungeonEntryFailure.AlreadyInDungeon, member.Slot);
            SelectedSessionIdentity? selected = null;
            if (context.DungeonId == CoupleDungeonRule.DungeonId)
            {
                selected = sessions?.Find(member.AccountId);
                if (selected?.CharacterId != member.CharacterId || !sessions!.IsInWorld(selected)) return Refuse(DungeonEntryFailure.MissingPrerequisite, member.Slot);
            }
            if (options.MirrorCatalog is { } catalog)
            {
                selected = sessions?.Find(member.AccountId);
                var position = onlineCharacters?.PositionOf(member.CharacterId);
                if (selected is null || selected.CharacterId != member.CharacterId || !sessions!.IsInWorld(selected) || position is null
                    || position.TownId != catalog.EntryTownId || position.AreaId != catalog.EntryAreaId)
                    return Refuse(DungeonEntryFailure.MissingPrerequisite, member.Slot);
            }
            if (characters is not null && (options.MirrorCatalog is not null || options.RequireMinimumLevel || !options.Mirror && definition is { HasStoryMode: false }))
            {
                var progress = await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
                if (progress is null || progress.Level < (options.MirrorCatalog?.MinimumLevel ?? definition!.MinimumRequiredLevel))
                    return Refuse(DungeonEntryFailure.LevelTooLow, member.Slot);
            }
            if (options.RequiredQuestIds.Count > 0)
            {
                var cleared = await (quests ?? throw new InvalidOperationException("Entry quest service unavailable."))
                    .LogOf(member.AccountId, member.CharacterId).ClearedAsync(ct).ConfigureAwait(false);
                if (options.RequiredQuestIds.Any(id => !cleared.Any(x => x.QuestId == id && (options.MirrorCatalog is null || x.Mark != 0))))
                    return Refuse(options.HellTickets is null ? DungeonEntryFailure.MissingPrerequisite : DungeonEntryFailure.HellQuestNotCleared, member.Slot);
            }
            if (!options.Mirror)
            {
                var maximum = await difficulties.GetMaximumAsync(member.CharacterId, context.DungeonId, ct).ConfigureAwait(false);
                var cycle = context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle;
                if (cycle && context.Facts.Intent.DirectedDungeonId != context.DungeonId)
                    return Refuse(DungeonEntryFailure.DifficultyNotUnlocked, member.Slot);
                bool allowed;
                try { allowed = cycle
                    ? await difficultyRules.IsUnlockedAsync(context.DungeonId, context.Facts.Difficulty, maximum, ct).ConfigureAwait(false)
                    : await difficultyRules.AllowsAsync(context.DungeonId, context.Facts.Difficulty, maximum, ct).ConfigureAwait(false); }
                catch (InvalidDataException error) { return new(null, new(null, DungeonEntryFailure.ConfigurationInvalid, member.Slot, error.Message)); }
                if (!allowed) return Refuse(DungeonEntryFailure.DifficultyNotUnlocked, member.Slot);
            }
            eligible.Add((member, selected));
        }
        foreach (var (member, selected) in eligible)
        {
            if ((options.ConsumesFatigue ?? definition?.ConsumesFatigue ?? true)
                && (await fatigue.BalanceOfAsync(member.CharacterId, ct).ConfigureAwait(false)).Remaining == 0)
                return Refuse(DungeonEntryFailure.InsufficientFatigue, member.Slot);
            var requirements = (options.RequiredItemsOverride ?? definition?.RequiredItems ?? []).ToImmutableArray();
            // Each group is AND. Event materials replace the entire regular group when available.
            // Explicit overrides remain authoritative for existing specialized entry paths.
            if (options.RequiredItemsOverride is null && definition?.EventRequiredItems is { Count: > 0 } eventItems
                && (await PrepareMaterialsAsync(member, eventItems, ct).ConfigureAwait(false)).Plan is not null)
                requirements = eventItems.ToImmutableArray();
            int? hellTicketItemId = null;
            if (options.HellTickets is { } alternatives)
            {
                ImmutableArray<DungeonRequiredItem>? chosen = null;
                foreach (var ticket in alternatives)
                {
                    var candidate = requirements.Add(new(ticket.ItemId, ticket.Count, true) { AllowSharedMaterial = false });
                    if ((await PrepareMaterialsAsync(member, candidate.Where(x => x.ItemId == ticket.ItemId).ToArray(), ct).ConfigureAwait(false)).Plan is not null)
                    { chosen = candidate; hellTicketItemId = ticket.ItemId; break; }
                }
                if (chosen is null) return Refuse(DungeonEntryFailure.MissingHellTicket, member.Slot);
                requirements = chosen.Value;
            }
            if (requirements.Length > 0 && (await PrepareMaterialsAsync(member, requirements, ct).ConfigureAwait(false)).Plan is null)
                return Refuse(DungeonEntryFailure.MissingRequiredItem, member.Slot);
            var costs = ImmutableArray.CreateBuilder<DungeonAdmissionCost>();
            if (options.GoldCosts.TryGetValue(member.CharacterId, out var goldCost))
            {
                if (goldCost < 0) throw new InvalidDataException("Negative dungeon entry gold cost.");
                if (await (characters ?? throw new InvalidOperationException("Character service unavailable.")).Get(member.CharacterId).GoldAsync(ct).ConfigureAwait(false) < goldCost)
                    return Refuse(DungeonEntryFailure.InsufficientGold, member.Slot);
                if (goldCost > 0) costs.Add(new DungeonGoldAdmissionCost(goldCost));
            }
            costs.AddRange(requirements.Where(x => x.Consume).Select(x => new DungeonMaterialAdmissionCost(x.ItemId, x.Count, x.AllowSharedMaterial)));
            if (options.Quota is { } limit)
            {
                var key = limit.Family == UsageQuotaFamily.GoldSilverHorn ? UsageQuotaKey.GoldSilverHorn(limit.Period) : limit.Family == UsageQuotaFamily.HerasDungeon ? UsageQuotaKey.Heras("entries", limit.Period) : limit.Family == UsageQuotaFamily.TournamentDungeon ? UsageQuotaKey.Tournament(limit.DungeonId ?? throw new InvalidDataException("Tournament quota requires dungeon id."), limit.Period) : limit.Family == UsageQuotaFamily.AltarDungeon ? UsageQuotaKey.Altar(limit.DungeonId ?? throw new InvalidDataException("Altar quota requires dungeon id."), limit.Period) : limit.Family == UsageQuotaFamily.MirrorDungeon ? UsageQuotaKey.Mirror("entries", limit.Period) : limit.Family == UsageQuotaFamily.DespairTower ? UsageQuotaKey.Despair("entries", limit.Period) : UsageQuotaKey.Dimension(limit.Period);
                UsageQuotaValue? expected;
                if (limit.Family == UsageQuotaFamily.MirrorDungeon)
                {
                    var state = await Quotas.ReadMirrorAsync(member.CharacterId, limit.Period, ct).ConfigureAwait(false);
                    expected = new(state.Entries, null);
                }
                else expected = await Quotas.ReadAsync(member.CharacterId, key, ct).ConfigureAwait(false);
                var used = expected?.UsedCount ?? 0;
                if (limit.Family == UsageQuotaFamily.DimensionDungeon ? used >= limit.Maximum : limit.Maximum > 0 && used >= limit.Maximum)
                    return Refuse(limit.Family == UsageQuotaFamily.DimensionDungeon && context.Participants.Count > 1
                        ? DungeonEntryFailure.MemberEntryLimitReached : DungeonEntryFailure.DailyEntryLimitReached, member.Slot);
                costs.Add(new DungeonQuotaAdmissionCost(key, expected, checked(used + 1)));
            }
            permits.Add(new Permit(member, costs.ToImmutable(), requirements, selected, hellTicketItemId));
        }
        return new(new Batch(this, transactions?.Current, context with { Participants = context.Participants.ToImmutableArray() },
            options with { ConsumesFatigue = options.ConsumesFatigue ?? definition?.ConsumesFatigue ?? true }, permits.ToImmutable()));
    }

    public async ValueTask<DungeonAdmissionConsumption> ConsumeAsync(IDungeonAdmissionBatch batch, CancellationToken ct)
    {
        if (batch is not Batch owned || !ReferenceEquals(owned.Owner, this) || owned.Consumed
            || !ReferenceEquals(owned.Transaction, transactions?.Current))
            throw new InvalidOperationException("Admission permits are foreign, stale or already consumed.");
        owned.Consumed = true;
        var plans = new List<IDungeonEntryMaterialPlan>();
        foreach (var raw in owned.Permits)
        {
            var permit = (Permit)raw;
            if (owned.Options.ConsumesFatigue == true && (await fatigue.BalanceOfAsync(permit.Participant.CharacterId, ct).ConfigureAwait(false)).Remaining == 0)
                return new(null, new(null, DungeonEntryFailure.InsufficientFatigue, permit.Participant.Slot));
            if (permit.Selected is { } selected && sessions?.IsInWorld(selected) != true)
                return new(null, new(null, DungeonEntryFailure.MissingPrerequisite, permit.Participant.Slot));
            if (permit.Requirements.Length > 0)
            {
                if (permit.HellTicketItemId is { } ticketId
                    && (await PrepareMaterialsAsync(permit.Participant, permit.Requirements.Where(x => x.ItemId == ticketId).ToArray(), ct).ConfigureAwait(false)).Plan is null)
                    return new(null, new(null, DungeonEntryFailure.MissingHellTicket, permit.Participant.Slot));
                var result = await PrepareMaterialsAsync(permit.Participant, permit.Requirements, ct).ConfigureAwait(false);
                if (result.Plan is null) return new(null, new(null, DungeonEntryFailure.MissingRequiredItem, permit.Participant.Slot));
                if (permit.Costs.OfType<DungeonMaterialAdmissionCost>().Any()) plans.Add(result.Plan);
            }
            foreach (var gold in permit.Costs.OfType<DungeonGoldAdmissionCost>())
                if (await (characters ?? throw new InvalidOperationException("Character service unavailable.")).Get(permit.Participant.CharacterId).GoldAsync(ct).ConfigureAwait(false) < gold.Amount)
                    return new(null, new(null, DungeonEntryFailure.InsufficientGold, permit.Participant.Slot));
            foreach (var quota in permit.Costs.OfType<DungeonQuotaAdmissionCost>())
            {
                var now = quota.Key.Family == UsageQuotaFamily.MirrorDungeon
                    ? new UsageQuotaValue((await Quotas.ReadMirrorAsync(permit.Participant.CharacterId, quota.Key.Day!.Value, ct).ConfigureAwait(false)).Entries, null)
                    : await Quotas.ReadAsync(permit.Participant.CharacterId, quota.Key, ct).ConfigureAwait(false);
                if (now != quota.Expected) return new(null, new(null, quota.Key.Family == UsageQuotaFamily.DimensionDungeon && owned.Permits.Length > 1
                    ? DungeonEntryFailure.MemberEntryLimitReached : DungeonEntryFailure.DailyEntryLimitReached, permit.Participant.Slot));
            }
        }
        if (owned.Permits.Any(x => x.Costs.Length > 0) && owned.Transaction is not { IsReadOnly: false })
            throw new InvalidOperationException("Admission charges require a writable entry transaction.");
        var changes = new List<DungeonEntryInventoryChange>();
        try
        {
            foreach (var plan in plans) changes.AddRange(await plan.ConsumeAsync(ct).ConfigureAwait(false));
            foreach (var raw in owned.Permits)
            {
                var member = raw.Participant;
                foreach (var gold in raw.Costs.OfType<DungeonGoldAdmissionCost>())
                    if (await (characters ?? throw new InvalidOperationException("Character service unavailable.")).Get(member.CharacterId).TrySpendGoldAsync(gold.Amount, ct).ConfigureAwait(false) is null)
                        throw new InvalidOperationException("Entry gold changed after preflight.");
                foreach (var quota in raw.Costs.OfType<DungeonQuotaAdmissionCost>())
                {
                    var bus = events ?? throw new InvalidOperationException("Entry event service unavailable.");
                    if (quota.Key.Family == UsageQuotaFamily.GoldSilverHorn)
                        await bus.DirectAsync(new GoldSilverHornEntryRecorded(member.CharacterId, quota.Key.Day!.Value, quota.Expected, quota.Used), ct).ConfigureAwait(false);
                    else if (quota.Key.Family == UsageQuotaFamily.HerasDungeon)
                        await bus.DirectAsync(new HerasQuotaRecorded(member.CharacterId, quota.Key.Day!.Value, "entries", quota.Expected, quota.Used), ct).ConfigureAwait(false);
                    else if (quota.Key.Family == UsageQuotaFamily.TournamentDungeon)
                        await bus.DirectAsync(new TournamentEntryRecorded(member.CharacterId, int.Parse(quota.Key.Name, System.Globalization.CultureInfo.InvariantCulture), quota.Key.Day!.Value, quota.Expected, quota.Used), ct).ConfigureAwait(false);
                    else if (quota.Key.Family == UsageQuotaFamily.AltarDungeon)
                        await bus.DirectAsync(new AltarEntryRecorded(member.CharacterId, int.Parse(quota.Key.Name, System.Globalization.CultureInfo.InvariantCulture), quota.Key.Day!.Value, quota.Expected, quota.Used), ct).ConfigureAwait(false);
                    else if (quota.Key.Family == UsageQuotaFamily.MirrorDungeon)
                        await bus.DirectAsync(new MirrorEntryRecorded([new(member.AccountId, member.CharacterId, quota.Key.Day!.Value,
                            quota.Expected!.UsedCount, quota.Used)]), ct).ConfigureAwait(false);
                    else if (quota.Key.Family == UsageQuotaFamily.DespairTower)
                        await bus.DirectAsync(new DespairQuotaRecorded(member.CharacterId, quota.Key.Day!.Value, "entries", quota.Expected, quota.Used), ct).ConfigureAwait(false);
                    else await bus.DirectAsync(new DimensionEntryRecorded([new(member.CharacterId, quota.Key.Day!.Value,
                        quota.Expected, quota.Used)]), ct).ConfigureAwait(false);
                }
            }
        }
        catch { owned.Transaction?.RequireRollback(); throw; }
        return new(new Receipt(owned, changes.ToImmutableArray()));
    }

    public DungeonEntryAttempt? BeginEntry(IDungeonAdmissionReceipt receipt, IReadOnlyList<DungeonRunParticipant> participants, int dungeonId)
    {
        if (receipt is not Receipt paid || !ReferenceEquals(paid.Batch.Owner, this) || paid.Started
            || !ReferenceEquals(paid.Batch.Transaction, transactions?.Current) || paid.Batch.Context.DungeonId != dungeonId
            || !paid.Batch.Context.Participants.SequenceEqual(participants))
            throw new InvalidOperationException("Entry receipt is foreign, stale or already used.");
        if (paid.Batch.Permits.Cast<Permit>().Any(x => x.Selected is { } selected && sessions?.IsInWorld(selected) != true))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Mirror participants changed during entry.");
        paid.Started = true;
        return null;
    }

    public async ValueTask<DungeonEntryAttempt> ExecuteAsync(DungeonEntryContext context, DungeonAdmissionOptions options,
        Func<IDungeonAdmissionReceipt, CancellationToken, ValueTask<DungeonEntryAttempt>> enter, CancellationToken ct)
    {
        var decision = await PrepareAsync(context, options, ct).ConfigureAwait(false);
        if (decision.Batch is null) return decision.Refusal!;
        var payment = await ConsumeAsync(decision.Batch, ct).ConfigureAwait(false);
        if (payment.Receipt is null) return payment.Refusal!;
        try
        {
            var result = await enter(payment.Receipt, ct).ConfigureAwait(false);
            if (result.Entry is null)
            {
                transactions?.Current?.RequireRollback();
                return result;
            }
            return result with { Entry = result.Entry with { InventoryChanges = payment.Receipt.InventoryChanges.Concat(result.Entry.InventoryChanges).ToArray() } };
        }
        catch { transactions?.Current?.RequireRollback(); throw; }
    }

    private IUsageQuotaDomainService Quotas => quotas ?? throw new InvalidOperationException("Entry quota service unavailable.");
    private ValueTask<DungeonEntryMaterialPreparation> PrepareMaterialsAsync(DungeonRunParticipant member,
        IReadOnlyList<DungeonRequiredItem> requirements, CancellationToken ct) =>
        (materials ?? throw new InvalidOperationException("Entry material service unavailable.")).PrepareAsync([member], requirements, ct);
    private static DungeonAdmissionDecision Refuse(DungeonEntryFailure failure, byte? slot = null) => new(null, new(null, failure, slot));
    private sealed record Permit(DungeonRunParticipant Participant, ImmutableArray<DungeonAdmissionCost> Costs,
        ImmutableArray<DungeonRequiredItem> Requirements, SelectedSessionIdentity? Selected, int? HellTicketItemId) : IDungeonAdmissionPermit;
    private sealed class Batch(DungeonAdmissionDomainService owner, IDomainTransaction? transaction, DungeonEntryContext context,
        DungeonAdmissionOptions options, ImmutableArray<IDungeonAdmissionPermit> permits) : IDungeonAdmissionBatch
    {
        public DungeonAdmissionDomainService Owner { get; } = owner;
        public IDomainTransaction? Transaction { get; } = transaction;
        public DungeonEntryContext Context { get; } = context;
        public DungeonAdmissionOptions Options { get; } = options;
        public ImmutableArray<IDungeonAdmissionPermit> Permits { get; } = permits;
        public bool Consumed { get; set; }
    }
    private sealed class Receipt(Batch batch, IReadOnlyList<DungeonEntryInventoryChange> inventoryChanges) : IDungeonAdmissionReceipt
    {
        public Batch Batch { get; } = batch;
        public IReadOnlyList<DungeonEntryInventoryChange> InventoryChanges { get; } = inventoryChanges;
        public bool Started { get; set; }
    }
}
