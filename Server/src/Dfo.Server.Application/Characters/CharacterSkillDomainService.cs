using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillDomainStoreService(
    CharacterSkillFactsReader facts,
    ICharacterSkillTable skills,
    ICharacterSkillRuleService rules,
    IDomainTransactionAccessor transactions,
    IPremiumServiceDomainService premiums,
    IPremiumServiceRuleService premiumRules,
    CharacterSkillRefundService refunds) : ICharacterSkillDomainStoreService
{
    public ICharacterSkillDomainService Get(CharacterId characterId, SkillGroup group, SkillBuildId build)
    {
        if (group is not (SkillGroup.Pve or SkillGroup.Duel))
            throw new ArgumentOutOfRangeException(nameof(group), group, null);
        return new CharacterSkillDomainService(characterId, group, build, facts, skills, rules,
            transactions, premiums, premiumRules, refunds);
    }
}

internal sealed class CharacterSkillDomainService(
    CharacterId characterId,
    SkillGroup group,
    SkillBuildId build,
    CharacterSkillFactsReader factsReader,
    ICharacterSkillTable skills,
    ICharacterSkillRuleService rules,
    IDomainTransactionAccessor transactions,
    IPremiumServiceDomainService premiums,
    IPremiumServiceRuleService premiumRules,
    CharacterSkillRefundService refunds) : ICharacterSkillDomainService
{
    private CharacterSkillPageState? FindPage(CharacterSkillFacts? facts) =>
        facts?.Pages.SingleOrDefault(page => page.PageIndex == build.Value);

    public async ValueTask<IReadOnlyList<CharacterSkillState>?> GetLearnedSkillsAsync(CancellationToken ct) =>
        FindPage(await factsReader.ReadAsync(characterId, group, ct).ConfigureAwait(false))?.Skills;

    public async ValueTask<IReadOnlyList<CharacterSkillState>?> GetHotkeySkillsAsync(CancellationToken ct) =>
        (await GetLearnedSkillsAsync(ct).ConfigureAwait(false))
            ?.Where(skill => SkillHotkeySlots.Contains(skill.Slot)).ToArray();

    public async ValueTask<CharacterSkillPagePoints?> GetPointsAsync(CancellationToken ct)
    {
        var facts = await factsReader.ReadAsync(characterId, group, ct).ConfigureAwait(false);
        if (facts is null || FindPage(facts) is not { } page)
            return null;

        var ledger = rules.GetLedger(facts.Career, facts.Level, [page], facts.PointBasis);
        var total = rules.GetTotalPoints(facts.Career.Job, facts.Level, facts.PointBasis);
        return new(total, ledger.For(build), ledger.MissingSkillIds);
    }

    public async ValueTask<bool> SwapSlotsAsync(int first, int second, CancellationToken ct)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill slot changes require a transaction.");
        if (first is < 0 or >= SkillHotkeySlots.SecondRowEnd || second is < 0 or >= SkillHotkeySlots.SecondRowEnd
            || !await factsReader.CanEditAsync(characterId, group, build, ct).ConfigureAwait(false))
            return false;

        var rows = await skills.ReadLevelsAsync(characterId, ct, group).ConfigureAwait(false);
        var pageRows = rows.Where(row => row.PageIndex == build.Value);
        if (pageRows.Any(row => row.Slot < 0)
            || !pageRows.Any(row => row.Slot == first || row.Slot == second))
            return false;

        await skills.SwapSlotsAsync(characterId, build.Value, first, second, ct, group).ConfigureAwait(false);
        return true;
    }

    internal static bool IsValidLearningRequest(IReadOnlyList<CharacterSkillLearningChange> changes) =>
        changes.Count is > 0 and <= byte.MaxValue
        && changes.All(change => change.SkillId != 0 && change.Refund <= 1 && change.Increment != 0)
        && changes.Select(change => change.SkillId).Distinct().Count() == changes.Count;

    public async ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session,
        IReadOnlyList<CharacterSkillLearningChange> changes, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (session.CharacterId != characterId)
            throw new InvalidOperationException("Skill learning actor does not own the bound character.");
        footprint.DemandEffects(new(session.AccountId, characterId, session));
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill learning requires a domain transaction.");
        if (!IsValidLearningRequest(changes))
            return new(CharacterSkillLearningRejection.InvalidRequest);
        var facts = await factsReader.ReadAsync(characterId, group, ct).ConfigureAwait(false);
        if (facts is null)
            return new(CharacterSkillLearningRejection.NoSession);
        if (FindPage(facts) is not { } page
            || !await factsReader.CanEditAsync(characterId, group, build, ct).ConfigureAwait(false))
            return new(CharacterSkillLearningRejection.PageUnavailable);

        var current = page.Skills;
        if (current.Select(skill => skill.SkillId).Distinct().Count() != current.Count)
            return new(CharacterSkillLearningRejection.IncompleteLedger);
        var learningLevel = facts.Level;
        if (await premiums.For(session.AccountId).HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, ct).ConfigureAwait(false))
            learningLevel = checked(learningLevel + premiumRules.LevelAllowance(PremiumBenefit.LearnSkillsEarly));
        if (changes.Any(change => change.Refund != 0))
            return await refunds.ApplyAsync(session, group, build, changes, facts, current, learningLevel, footprint, ct)
                .ConfigureAwait(false);

        var target = current.ToDictionary(skill => checked((int)skill.SkillId));
        var occupied = current.Select(skill => (int)skill.Slot).ToHashSet();
        var changed = new List<CharacterSkillState>(changes.Count);
        foreach (var change in changes)
        {
            var rule = rules.Get(facts.Career.Job, change.SkillId);
            if (rule is null)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var existing = target.GetValueOrDefault(change.SkillId);
            var level = (existing?.Level ?? 0) + change.Increment;
            if (level > byte.MaxValue)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var slot = existing?.Slot ?? rule.FindAvailableSlot(occupied);
            if (slot is null)
                return new(CharacterSkillLearningRejection.NoSlot);
            if (slot is < 0 or >= byte.MaxValue || existing?.ExtraValues.Count > byte.MaxValue)
                return new(CharacterSkillLearningRejection.InvalidRequest);
            var learned = new CharacterSkillState(slot.Value, change.SkillId, (byte)level, existing?.ExtraValues ?? []);
            occupied.Add(slot.Value);
            target[change.SkillId] = learned;
            changed.Add(learned);
        }

        var finalLevels = target.ToDictionary(pair => pair.Key, pair => (int)pair.Value.Level);
        foreach (var learned in changed)
            if (!rules.Get(facts.Career.Job, (int)learned.SkillId)!
                .CanLearn(facts.Career, learningLevel, learned.Level, finalLevels))
                return new(CharacterSkillLearningRejection.SkillUnavailable);
        var pages = facts.Pages.Where(value => value.PageIndex != build.Value)
            .Append(new CharacterSkillPageState(build.Value, target.Values.OrderBy(skill => skill.Slot).ToArray()))
            .ToArray();
        var ledger = rules.GetLedger(facts.Career, facts.Level, pages, facts.PointBasis);
        if (!ledger.IsComplete)
            return new(CharacterSkillLearningRejection.IncompleteLedger);
        if (ledger.For(build) is { Sp: < 0 } or { Tp: < 0 })
            return new(CharacterSkillLearningRejection.InsufficientPoints);

        // Storage failures must roll back the entire accepted batch.
        foreach (var learned in changed)
        {
            footprint.DemandEffects(new(session.AccountId, characterId, session));
            if (current.Any(skill => skill.SkillId == learned.SkillId))
            {
                if (await skills.SetLevelAsync(characterId, build.Value, learned.Slot, learned.Level, ct, group)
                    .ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The learned skill row changed during its transaction.");
            }
            else
                await skills.InsertAsync(characterId, build.Value, learned.Slot, (int)learned.SkillId, learned.Level, ct, group)
                    .ConfigureAwait(false);
        }
        return new(CharacterSkillLearningRejection.None, new(session, build.Value, changed.AsReadOnly(), ledger));
    }
}
