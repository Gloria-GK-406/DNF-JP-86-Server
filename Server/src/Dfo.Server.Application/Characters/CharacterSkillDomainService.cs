using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillDomainService(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterSkillBuildTable builds,
    ICharacterEntryTable entries,
    ICharacterSkillRuleService rules) : ICharacterSkillDomainService
{
    public ValueTask<CharacterSkillPageSnapshot?> GetAsync(CharacterId characterId, SkillGroup group,
        SkillBuildId build, CancellationToken cancellationToken) => group switch
    {
        SkillGroup.Pve => GetOrdinaryPageAsync(characterId, build, cancellationToken),
        SkillGroup.Duel => GetDuelPageAsync(characterId, build, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    private async ValueTask<CharacterSkillPageSnapshot?> GetOrdinaryPageAsync(
        CharacterId characterId, SkillBuildId build, CancellationToken ct)
    {
        var facts = await ReadOrdinaryFactsAsync(characterId, ct).ConfigureAwait(false);
        if (facts?.Pages.SingleOrDefault(page => page.PageIndex == build.Value) is not { } selected)
            return null;

        var ledger = rules.GetLedger(facts.Career, facts.Level, [selected], facts.PointBasis);
        var total = rules.GetTotalPoints(facts.Career.Job, facts.Level, facts.PointBasis);
        return Snapshot(selected, new(total, ledger.For(build), ledger.MissingSkillIds));
    }

    private async ValueTask<CharacterSkillPageSnapshot?> GetDuelPageAsync(
        CharacterId characterId, SkillBuildId build, CancellationToken ct)
    {
        if (build.Value > 1 || await characters.FindStandingAsync(characterId, ct).ConfigureAwait(false) is null)
            return null;

        var pages = await ReadDuelPagesAsync(characterId, ct).ConfigureAwait(false);
        return Snapshot(pages[build.Value], points: null);
    }

    private static CharacterSkillPageSnapshot Snapshot(CharacterSkillPageState page, CharacterSkillPagePoints? points) =>
        new(page, page.Skills.Where(skill => SkillHotkeySlots.Contains(skill.Slot)).ToArray(), points);

    internal async ValueTask<CharacterSkillPointLedger?> ReadOrdinaryLedgerAsync(
        CharacterId characterId, CancellationToken ct)
    {
        var facts = await ReadOrdinaryFactsAsync(characterId, ct).ConfigureAwait(false);
        return facts is null ? null : rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis);
    }

    internal async ValueTask<CharacterSkillFacts?> ReadOrdinaryFactsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        if (await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } standing)
            return null;
        var stored = CharacterSkillPages.Pages(CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false)));
        var configurations = await builds.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        var pages = configurations.Builds.Select(build => stored.SingleOrDefault(page => page.PageIndex == build.Id.Value)
            ?? new CharacterSkillPageState(build.Id.Value, [])).ToArray();
        if (stored.Any(page => configurations.Builds.All(build => build.Id.Value != page.PageIndex)))
            throw new InvalidDataException("Skill rows reference an unknown build.");
        var bonus = await entries.FindBonusPointsAsync(characterId, cancellationToken).ConfigureAwait(false);
        return new CharacterSkillFacts(
            CharacterCareer.FromPacked(checked((byte)standing.Job), checked((byte)standing.GrowType)),
            standing.Level, pages, new CharacterSkillPointBasisState(bonus?.BonusSp ?? 0, bonus?.BonusTp ?? 0));
    }

    internal async ValueTask<IReadOnlyList<CharacterSkillPageState>> ReadDuelPagesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var pages = new[] { new List<CharacterSkillState>(), new List<CharacterSkillState>() };
        foreach (var row in await skills.ReadDuelSkillsAsync(characterId, cancellationToken).ConfigureAwait(false))
        {
            if (row.PageIndex is < 0 or > 1
                || row.Slot is < short.MinValue or > short.MaxValue
                || row.SkillId is < 0 or > ushort.MaxValue
                || row.Level is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("PvP skill profile contains an out-of-range row.");
            }

            pages[row.PageIndex].Add(new CharacterSkillState(
                checked((short)row.Slot), checked((ushort)row.SkillId),
                checked((byte)row.Level), row.ExtraValues));
        }

        return [new(0, pages[0]), new(1, pages[1])];
    }
}
