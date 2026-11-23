using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Accounts;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillStateDomainService(
    ICharacterTable characters, ICharacterSkillTable skills, CharacterSkillFactsReader skillFacts,
    ICharacterSkillRuleService rules, IDomainTransactionAccessor transactions, ICharacterSkillBuildTable builds,
    IPremiumServiceDomainService premiums, IPremiumServiceRuleService premiumRules,
    ICharacterPvpSkillStateTable duelBuilds,
    IExpertJobStateStore expertJobs, IExpertJobRuleService expertJobRules)
    : ICharacterSkillStateDomainService
{
    public async ValueTask<CharacterSkillBuilds> BuildsAsync(CharacterId id, CancellationToken ct,
        SkillGroup group = SkillGroup.Pve)
    {
        var ordinary = await builds.ReadAsync(id, ct).ConfigureAwait(false);
        if (group == SkillGroup.Pve)
            return ordinary;

        var duel = await duelBuilds.FindAsync(id, ct).ConfigureAwait(false);
        var unlocked = ordinary.IsUnlocked(new(1));
        return new(new(unlocked ? duel?.ActiveSkillBuild ?? 0 : 0),
            [new(new(0), true), new(new(1), unlocked)]);
    }

    public async ValueTask UnlockAsync(CharacterId id, SkillBuildId build, CancellationToken ct)
    {
        RequireTransaction();
        await builds.AddAsync(id, build, ct);
        await ReconcileAsync(id, ct);
    }

    public async ValueTask ActivateAsync(CharacterId id, SkillBuildId build, CancellationToken ct,
        SkillGroup group = SkillGroup.Pve)
    {
        RequireTransaction();
        if (group == SkillGroup.Pve)
        {
            await builds.SetActiveAsync(id, build, ct).ConfigureAwait(false);
            return;
        }

        if (!(await BuildsAsync(id, ct, group).ConfigureAwait(false)).IsUnlocked(build)
            || !await duelBuilds.SetActiveBuildAsync(id, build, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Duel skill build is locked or unavailable.");
    }

    public async ValueTask ResetAsync(CharacterId id, SkillBuildId build, CancellationToken ct)
    {
        RequireTransaction();
        var current = await builds.ReadAsync(id, ct);
        if (!current.IsUnlocked(build)) throw new InvalidOperationException("Skill build is locked or unavailable.");
        await ResetPagesAsync(id, new HashSet<int> { build.Value }, ct);
    }

    public async ValueTask ResetAllAsync(CharacterId id, CancellationToken ct)
    {
        RequireTransaction();
        var current = await builds.ReadAsync(id, ct);
        // Include allocated locked pages so unlocking cannot resurrect pre-reset learning.
        await ResetPagesAsync(id, current.Builds.Select(build => build.Id.Value).ToHashSet(), ct);
    }

    private async ValueTask ResetPagesAsync(CharacterId id, IReadOnlySet<int> pages, CancellationToken ct)
    {
        await builds.EnsureAsync(id, ct);
        var facts = await ReadAsync(id, ct) ?? throw new InvalidOperationException("Character is unavailable.");
        var definitions = rules.GetAll(facts.Career.Job);
        // Resolve/validate before the first destructive write.
        _ = rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis);
        foreach (var rule in definitions) _ = rule.GetFreeLevel(facts.Career, facts.Level);
        foreach (var page in facts.Pages.Where(page => pages.Contains(page.PageIndex)))
            foreach (var skill in page.Skills)
                if (await skills.RemoveAsync(id, page.PageIndex, skill.Slot, ct) != 1)
                    throw new InvalidOperationException("Skill row changed during reset.");
        await ReconcilePagesAsync(id, pages, ct);
    }

    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill mutation requires a domain transaction.");
    }

    public async ValueTask ReconcileLevelAsync(CharacterId id, CancellationToken ct)
    {
        RequireTransaction();
        var facts = await ReadAsync(id, ct) ?? throw new InvalidOperationException("Character is unavailable.");
        // Unknown learning cannot be safely priced or reduced.
        if (!rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).IsComplete)
            throw new InvalidDataException("Cannot change level with unknown learned skill rules.");
        await ReconcileAsync(id, ct);
        var expertJobSkills = await ExpertJobSkillsAsync(id, ct).ConfigureAwait(false);
        var learningLevel = await LearningLevelAsync(id, facts.Level, ct);
        bool changed;
        do
        {
            changed = false;
            facts = await ReadAsync(id, ct) ?? throw new InvalidOperationException("Character is unavailable.");
            foreach (var page in facts.Pages)
            {
                var learnedLevels = page.Skills.ToDictionary(s => checked((int)s.SkillId), s => (int)s.Level);
                foreach (var learned in page.Skills)
                {
                    var rule = rules.Get(facts.Career.Job, checked((int)learned.SkillId))
                        ?? throw new InvalidDataException("Unknown learned skill.");
                    var free = Math.Max(rule.GetFreeLevel(facts.Career, facts.Level), expertJobSkills.GetValueOrDefault(rule.SkillId));
                    var allowed = Math.Min(learned.Level, Math.Max(rule.GetMaxLevel(facts.Career, learningLevel) ?? 0, free));
                    while (allowed > free && !rule.CanLearn(facts.Career, learningLevel, allowed, learnedLevels)) allowed--;
                    if (learned.Level == allowed) continue;
                    var touched = allowed == 0 ? await skills.RemoveAsync(id, page.PageIndex, learned.Slot, ct)
                        : await skills.SetLevelAsync(id, page.PageIndex, learned.Slot, allowed, ct);
                    if (touched != 1) throw new InvalidOperationException("Skill changed during level reconciliation.");
                    if (allowed == 0) learnedLevels.Remove(rule.SkillId); else learnedLevels[rule.SkillId] = allowed;
                    changed = true;
                }
            }
        } while (changed);
        facts = await ReadAsync(id, ct) ?? throw new InvalidOperationException("Character is unavailable.");
        var ledger = rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis);
        foreach (var page in facts.Pages)
        {
            var remaining = ledger.For(new(page.PageIndex));
            if (remaining.Sp < 0 || remaining.Tp < 0) await ResetAsync(id, new(page.PageIndex), ct);
        }
    }

    private async ValueTask<int> LearningLevelAsync(CharacterId id, int level, CancellationToken ct)
    {
        var owner = await characters.FindOwnerAsync(id, ct) ?? throw new InvalidOperationException("Character is unavailable.");
        return level + (await premiums.For(owner).HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, ct)
            ? premiumRules.LevelAllowance(PremiumBenefit.LearnSkillsEarly) : 0);
    }

    public ValueTask ReconcileAsync(CharacterId characterId, CancellationToken cancellationToken) =>
        ReconcilePagesAsync(characterId, null, cancellationToken);

    private async ValueTask ReconcilePagesAsync(CharacterId characterId, IReadOnlySet<int>? selected, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill reconciliation requires a domain transaction.");
        var facts = await ReadAsync(characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Skill reconciliation character is unavailable.");
        var available = rules.GetAll(facts.Career.Job).ToDictionary(rule => rule.SkillId);
        var expertJobSkills = await ExpertJobSkillsAsync(characterId, cancellationToken).ConfigureAwait(false);
        var learningLevel = await LearningLevelAsync(characterId, facts.Level, cancellationToken);
        // Validate the career even for an empty stored skill tree.
        _ = rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis);
        await builds.EnsureAsync(characterId, cancellationToken).ConfigureAwait(false);
        foreach (var page in (await builds.ReadAsync(characterId, cancellationToken)).Builds.Select(build => build.Id.Value))
        {
            if (selected is not null && !selected.Contains(page)) continue;
            var retained = new Dictionary<int, CharacterSkillState>();
            foreach (var learned in facts.Pages.SingleOrDefault(value => value.PageIndex == page)?.Skills ?? [])
            {
                if (!available.TryGetValue(checked((int)learned.SkillId), out var rule))
                {
                    retained.Add(checked((int)learned.SkillId), learned);
                    continue;
                }
                if (rule.GetMaxLevel(facts.Career, learningLevel) is null && rule.GetFreeLevel(facts.Career, facts.Level) == 0
                    && !expertJobSkills.ContainsKey(rule.SkillId))
                {
                    if (await skills.RemoveAsync(characterId, page, learned.Slot, cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Skill row changed during reconciliation.");
                }
                else retained.Add(rule.SkillId, learned);
            }
            var occupied = retained.Values.Select(value => (int)value.Slot).ToHashSet();
            foreach (var rule in available.Values.OrderBy(rule => rule.SkillId))
            {
                var free = Math.Max(rule.GetFreeLevel(facts.Career, facts.Level), expertJobSkills.GetValueOrDefault(rule.SkillId));
                if (free <= 0) continue;
                var level = checked((byte)free);
                if (retained.TryGetValue(rule.SkillId, out var learned))
                {
                    // Career reconciliation does not revoke level allowances; contract expiry owns that behavior.
                    if (level > learned.Level && await skills.SetLevelAsync(characterId, page, learned.Slot,
                            level, cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Skill row changed during reconciliation.");
                }
                else
                {
                    var slot = rule.FindAvailableSlot(occupied)
                        ?? throw new InvalidDataException("No slot for a granted skill.");
                    await skills.InsertAsync(characterId, page, slot, rule.SkillId, level, cancellationToken).ConfigureAwait(false);
                    occupied.Add(slot);
                }
            }
        }
    }

    private async ValueTask<IReadOnlyDictionary<int, byte>> ExpertJobSkillsAsync(CharacterId character, CancellationToken ct)
    {
        var state = await expertJobs.ReadAsync(character, ct).ConfigureAwait(false);
        return (await expertJobRules.FindAsync(state.Type, ct).ConfigureAwait(false))?.Skills
            ?? System.Collections.Immutable.ImmutableDictionary<int, byte>.Empty;
    }

    public ValueTask<CharacterSkillFacts?> ReadAsync(CharacterId characterId, CancellationToken cancellationToken,
        SkillGroup group = SkillGroup.Pve) =>
        skillFacts.ReadAsync(characterId, group, cancellationToken);
}
