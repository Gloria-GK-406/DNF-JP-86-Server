using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillFactsReader(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterSkillBuildTable builds,
    ICharacterEntryTable entries,
    ICharacterPvpTable pvp)
{
    public async ValueTask<bool> CanEditAsync(CharacterId characterId, SkillGroup group, SkillBuildId build,
        CancellationToken ct)
    {
        if (await characters.FindOwnerAsync(characterId, ct).ConfigureAwait(false) is null)
            return false;
        return group switch
        {
            SkillGroup.Pve => (await builds.ReadAsync(characterId, ct).ConfigureAwait(false)).IsUnlocked(build),
            // Entry seeds the duel group once. Writes before seeding could collide with its initial skills.
            SkillGroup.Duel => build.Value <= 1
                && (await pvp.FindAsync(characterId, ct).ConfigureAwait(false)) is { SkillsInitialized: not 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
        };
    }

    public async ValueTask<CharacterSkillFacts?> ReadAsync(
        CharacterId characterId, SkillGroup group, CancellationToken cancellationToken)
    {
        if (await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } standing)
            return null;
        var pages = group switch
        {
            SkillGroup.Pve => await ReadOrdinaryPagesAsync(characterId, cancellationToken).ConfigureAwait(false),
            SkillGroup.Duel => await ReadDuelPagesAsync(characterId, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
        };
        var bonus = await entries.FindBonusPointsAsync(characterId, cancellationToken).ConfigureAwait(false);
        return new CharacterSkillFacts(
            CharacterCareer.FromPacked(checked((byte)standing.Job), checked((byte)standing.GrowType)),
            standing.Level, pages, new CharacterSkillPointBasisState(bonus?.BonusSp ?? 0, bonus?.BonusTp ?? 0));
    }

    private async ValueTask<IReadOnlyList<CharacterSkillPageState>> ReadOrdinaryPagesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var stored = CharacterSkillPages.Pages(CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false)));
        var configurations = await builds.ReadAsync(characterId, cancellationToken).ConfigureAwait(false);
        var pages = configurations.Builds.Select(build => stored.SingleOrDefault(page => page.PageIndex == build.Id.Value)
            ?? new CharacterSkillPageState(build.Id.Value, [])).ToArray();
        if (stored.Any(page => configurations.Builds.All(build => build.Id.Value != page.PageIndex)))
            throw new InvalidDataException("Skill rows reference an unknown build.");
        return pages;
    }

    internal async ValueTask<IReadOnlyList<CharacterSkillPageState>> ReadDuelPagesAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var pages = new[] { new List<CharacterSkillState>(), new List<CharacterSkillState>() };
        foreach (var row in await skills.ReadEntryAsync(characterId, cancellationToken, SkillGroup.Duel).ConfigureAwait(false))
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
