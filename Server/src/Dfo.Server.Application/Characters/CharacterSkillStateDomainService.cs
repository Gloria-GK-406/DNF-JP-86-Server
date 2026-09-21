using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Transactions;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillStateDomainService(
    ICharacterTable characters, ICharacterSkillTable skills, ICharacterEntryTable entries,
    ICharacterSkillRuleService rules, IDomainTransactionAccessor transactions)
    : ICharacterSkillStateDomainService
{
    public async ValueTask ReconcileAsync(CharacterId characterId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill reconciliation requires a domain transaction.");
        var facts = await ReadAsync(characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Skill reconciliation character is unavailable.");
        var available = rules.GetAll(facts.Career.Job).ToDictionary(rule => rule.SkillId);
        // Validate the career even for an empty stored skill tree.
        _ = rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis);
        for (var page = 0; page < 2; page++)
        {
            var retained = new Dictionary<int, CharacterSkillState>();
            foreach (var learned in facts.Pages.SingleOrDefault(value => value.PageIndex == page)?.Skills ?? [])
            {
                if (!available.TryGetValue(checked((int)learned.SkillId), out var rule))
                {
                    retained.Add(checked((int)learned.SkillId), learned);
                    continue;
                }
                if (rule.GetMaxLevel(facts.Career, facts.Level) is null && rule.GetFreeLevel(facts.Career, facts.Level) == 0)
                {
                    if (await skills.RemoveAsync(characterId, page, learned.Slot, cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Skill row changed during reconciliation.");
                }
                else retained.Add(rule.SkillId, learned);
            }
            var occupied = retained.Values.Select(value => (int)value.Slot).ToHashSet();
            foreach (var rule in available.Values.OrderBy(rule => rule.SkillId))
            {
                var free = rule.GetFreeLevel(facts.Career, facts.Level);
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

    public async ValueTask<CharacterSkillFacts?> ReadAsync(CharacterId characterId, CancellationToken cancellationToken)
    {
        if (await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } standing)
            return null;
        var pages = CharacterSkillPages.Pages(CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false)));
        var bonus = await entries.FindBonusPointsAsync(characterId, cancellationToken).ConfigureAwait(false);
        return new CharacterSkillFacts(
            CharacterCareer.FromPacked(checked((byte)standing.Job), checked((byte)standing.GrowType)),
            standing.Level, pages, new CharacterSkillPointBasisState(bonus?.BonusSp ?? 0, bonus?.BonusTp ?? 0));
    }
}
