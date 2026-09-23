using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// The stored skill rows, read into the pages everything above reasons about.
/// </summary>
/// <remarks>
/// One mapping with two readers. Entry builds a character's pages on the way in; a character asked
/// later what it has left to spend builds them again from the same rows. What each page holds
/// decides what the points cost, so the two readings have to agree exactly -- and a second copy of
/// this that dropped a page, or took a level from the wrong column, would come out with a
/// plausible number that nothing could catch.
/// <para>
/// The rows arrive already narrowed to one set of pages by the statement that read them; nothing
/// here chooses between the ordinary and the duel pages.
/// </para>
/// </remarks>
internal static class CharacterSkillPages
{
    /// <summary>The stored rows, refused if any of them is out of the shape a page can hold.</summary>
    /// <remarks>
    /// Refused rather than skipped. A row this cannot represent is a row whose skill would silently
    /// cost nothing, and a ledger short one skill's worth of points is indistinguishable from a
    /// character who never learned it.
    /// </remarks>
    public static IReadOnlyList<CharacterSkillStorageRow> Rows(
        IReadOnlyList<CharacterSkillEntryRow> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var rows = new List<CharacterSkillStorageRow>(stored.Count);
        foreach (var skill in stored)
        {
            if (skill.PageIndex < 0 || skill.Slot is < 0 or > short.MaxValue
                || skill.SkillId <= 0 || skill.Level is < 0 or > byte.MaxValue)
            {
                throw new InvalidDataException("Character skill state is out of range.");
            }

            rows.Add(new CharacterSkillStorageRow(
                skill.PageIndex, checked((short)skill.Slot), skill.SkillId,
                skill.Level, skill.ExtraValues));
        }

        return rows;
    }

    /// <summary>The rows gathered into pages, in page order and slot order within each.</summary>
    public static IReadOnlyList<CharacterSkillPageState> Pages(
        IReadOnlyList<CharacterSkillStorageRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return
        [
            .. rows
                .GroupBy(value => value.PageIndex)
                .OrderBy(group => group.Key)
                .Select(group => new CharacterSkillPageState(
                    group.Key,
                    group.OrderBy(value => value.Slot)
                        .Select(value => new CharacterSkillState(
                            value.Slot,
                            checked((uint)value.SkillId),
                            checked((byte)value.Level),
                            [.. value.ExtraValues]))
                        .ToArray())),
        ];
    }
}
