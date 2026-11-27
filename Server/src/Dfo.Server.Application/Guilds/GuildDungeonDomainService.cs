using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildDungeonDomainService(GuildDungeonRuleService rules, IGuildDungeonStore progress,
    IGuildDomainStore guilds, IGuildPointAccount pointAccount, ISelectedSessionDomainService sessions, IDungeonEntryIntentDomainService intents,
    IDungeonAdmissionDomainService admission, IDungeonVisitDomainService visits, IDungeonStructureRuleService layouts,
    IItemDeliveryDomainService items, IEventService events, TimeProvider clock, ICharacterDomainService characters)
    : IGuildDungeonDomainService
{
    public bool Supports(int dungeon) => rules.Supports(dungeon);

    public async ValueTask<bool> TryExtendAsync(CharacterId character, int guildPoints, DateTimeOffset now, CancellationToken ct)
    {
        if (guildPoints <= 0 || rules.Catalog() is not { } catalog
            || await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { Member.IsLeader: true } member)
            return false;
        var guild = member.Guild.Id;
        var calendar = GuildDungeonPeriod.At(now);
        if (await progress.ResolvePeriodAsync(guild, calendar, ct).ConfigureAwait(false) != calendar
            || !await pointAccount.TrySpendAsync(guild, guildPoints, ct).ConfigureAwait(false))
            return false;

        var extended = calendar.SkipNextReset();
        await progress.AddExtensionAsync(guild, extended, ct).ConfigureAwait(false);
        await events.EnqueueAsync(new GuildDungeonProgressChanged(character, new(guild, catalog.StyleId, extended))
        {
            RefreshGuildInfo = true,
        }, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<GuildDungeonAdmission> CurrentAdmissionAsync(GuildId guild, int style,
        DateTimeOffset now, CancellationToken ct) =>
        new(guild, style, await progress.ResolvePeriodAsync(guild, GuildDungeonPeriod.At(now), ct).ConfigureAwait(false));

    public async ValueTask<int?> ReadMedalShopAsync(GuildId guild, DateTimeOffset now, CancellationToken ct)
    {
        if (rules.Catalog() is not { } catalog)
            return null;
        var period = await CurrentAdmissionAsync(guild, catalog.StyleId, now, ct).ConfigureAwait(false);
        var counts = await progress.ReadProgressAsync(period, ct).ConfigureAwait(false);
        return GuildDungeonRuleService.MedalShopFor(catalog, period, counts);
    }

    public async ValueTask<GuildDungeonView?> ReadAsync(CharacterId character, CancellationToken ct)
    {
        if (rules.Catalog() is not { } catalog ||
            await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } member)
            return null;

        var identity = await CurrentAdmissionAsync(member.Guild.Id, catalog.StyleId, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var counts = await progress.ReadProgressAsync(identity, ct).ConfigureAwait(false);
        return new(identity, catalog.Dungeons.Keys.Order()
            .Select(id => new GuildDungeonProgress(id, counts.GetValueOrDefault(id))).ToArray())
        {
            MedalShopId = GuildDungeonRuleService.MedalShopFor(catalog, identity, counts),
        };
    }

    public async ValueTask<int?> ReadRepeatBonusAsync(CharacterId character, CancellationToken ct) =>
        rules.Catalog()?.RepeatBonus is null ? null : await progress.ReadRepeatBonusAsync(character, ct).ConfigureAwait(false);

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext entry, CancellationToken ct)
    {
        if (rules.Catalog() is not { } catalog || !catalog.Dungeons.TryGetValue(entry.DungeonId, out var definition))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        if (entry.RequestMode != 0 || entry.PracticeMode != 0 || entry.MirrorQuestId is not null || entry.Participants.Count == 0
            || entry.Facts.Intent.GuildSelection is not { } selection)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);

        TownAreaKey? gate = null;
        var entryMemberships = new Dictionary<CharacterId, long>();
        var entryLevels = new Dictionary<CharacterId, int>();
        foreach (var member in entry.Participants)
        {
            if (sessions.Find(member.AccountId) is not { } selected || selected.CharacterId != member.CharacterId
                || !sessions.IsInWorld(selected)
                || !ReferenceEquals(intents.Find(selected)?.GuildSelection, selection)
                || await guilds.FindMembershipAsync(member.CharacterId, ct).ConfigureAwait(false) is not { } membership
                || selection.GateFor(selected, membership) is not { } origin || gate is not null && gate != origin)
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot,
                    "Guild dungeon requires a single guild and its hall's dungeon gate.");
            gate = origin;
            entryMemberships.Add(member.CharacterId, membership.Member.MembershipId);
            if (catalog.RepeatBonus is not null)
            {
                var progression = await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
                if (progression is null)
                    return new(null, DungeonEntryFailure.LevelTooLow, member.Slot);
                entryLevels.Add(member.CharacterId, progression.Level);
            }
        }

        var plan = await CurrentAdmissionAsync(new GuildId((uint)gate!.Value.Instance), catalog.StyleId, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var counts = await progress.ReadProgressAsync(plan, ct).ConfigureAwait(false);
        if (!GuildDungeonRuleService.PrerequisitesMet(catalog, definition, counts))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Guild conquest prerequisite is incomplete.");

        var mode = new DungeonRunMode.GuildDungeon(plan)
        {
            EntryMemberships = entryMemberships,
            EntryLevels = entryLevels,
            PartyBuffId = GuildDungeonRuleService.PartyBuffFor(catalog, entry.Participants.Count),
        };
        return await admission.ExecuteAsync(entry, new() { RequireMinimumLevel = true },
            (receipt, token) => visits.ResolveAndEnterAsync(layouts, entry.Participants, entry.DungeonId,
                entry.Facts, receipt, layout => layout with { Mode = mode }, token),
            ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> SettleAsync(AccountId account, CharacterId character, DungeonRunState run,
        DateTimeOffset now, CancellationToken ct)
    {
        if (run.Layout.Mode is not DungeonRunMode.GuildDungeon mode || !run.ClearOpen
            || !run.Participants.Any(member => member.CharacterId == character && member.AccountId == account)
            || rules.Catalog() is not { } catalog
            || mode.Admission.StyleId != catalog.StyleId || !catalog.Dungeons.TryGetValue(run.Layout.DungeonId, out var dungeon)
            || await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } membership
            || !mode.Includes(membership))
            return false;

        var entry = await CurrentAdmissionAsync(mode.Admission.Guild, catalog.StyleId, now, ct).ConfigureAwait(false);
        if (mode.Admission.Period.StartUnix != entry.Period.StartUnix)
            return false;
        var counts = await progress.ReadProgressAsync(entry, ct).ConfigureAwait(false);
        var reward = GuildDungeonRuleService.RewardFor(dungeon, counts.GetValueOrDefault(dungeon.Id));
        if (!await progress.TryRecordClearAsync(run.RunId.Value, character, entry, dungeon.Id, reward.Contribution, ct)
                .ConfigureAwait(false))
            return false;

        var mail = new ItemDeliveryOptions(null, $"guild-dungeon:{run.RunId.Value:N}:{character.Value}")
        {
            MailExpiresAt = now.AddDays(30),
        };
        await items.DeliverAsync(account, character, [new(reward.ItemId, reward.Count)], "公会地下城通关奖励",
            ct, ItemDeliveryMode.MailOnly, mail)
            .ConfigureAwait(false);
        var bonusAdvanced = false;
        if (catalog.RepeatBonus is { } bonus
            && run.ClearParticipants.Any(member => member.CharacterId == character && member.AccountId == account))
        {
            bonusAdvanced = true;
            var levels = run.ClearParticipants.Select(member => mode.EntryLevels[member.CharacterId]).ToArray();
            var current = await progress.ReadRepeatBonusAsync(character, ct).ConfigureAwait(false);
            var (next, grantReward) = GuildDungeonRuleService.EvaluateRepeatBonus(bonus, current, levels);
            await progress.WriteRepeatBonusAsync(character, next, ct).ConfigureAwait(false);
            if (grantReward)
            {
                await items.DeliverAsync(account, character, [new(bonus.ItemId, bonus.Count)], "公会地下城奖励礼盒",
                    ct, ItemDeliveryMode.MailOnly, new(null, $"guild-dungeon-repeat:{run.RunId.Value:N}:{character.Value}")
                    {
                        MailExpiresAt = now.AddDays(30),
                    }).ConfigureAwait(false);
            }
        }
        await events.EnqueueAsync(new GuildDungeonProgressChanged(character, entry) { RepeatBonusAdvanced = bonusAdvanced }, ct)
            .ConfigureAwait(false);
        return true;
    }
}

internal sealed class GuildDungeonProgressSubscriber(IGuildDungeonDomainService dungeons, IGuildDomainStore guilds,
    IEventService events) : IDomainEventSubscriber<GuildDungeonProgressChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<GuildDungeonProgressChanged> message, CancellationToken cancellationToken)
    {
        var changed = message.Payload;
        if (changed.RepeatBonusAdvanced
            && await dungeons.ReadRepeatBonusAsync(changed.Character, cancellationToken).ConfigureAwait(false) is { } bonus)
            await events.ProjectAsync(new NotificationAudience.TheseCharacters([changed.Character]),
                new GuildDungeonRepeatBonusNotification(bonus), cancellationToken).ConfigureAwait(false);
        var view = await dungeons.ReadAsync(changed.Character, cancellationToken).ConfigureAwait(false);
        if (view is null || view.Admission.Guild != changed.Admission.Guild
            || view.Admission.StyleId != changed.Admission.StyleId
            || view.Admission.Period.StartUnix != changed.Admission.Period.StartUnix) return;
        var members = await guilds.ReadRosterCharacterIdsAsync(changed.Character, cancellationToken).ConfigureAwait(false);
        await events.ProjectAsync(new NotificationAudience.TheseCharacters(members),
            new GuildDungeonProgressNotification(view) { RefreshGuildInfo = changed.RefreshGuildInfo }, cancellationToken).ConfigureAwait(false);
    }
}
