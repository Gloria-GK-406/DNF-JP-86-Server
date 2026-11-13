namespace Dfo.Server.Abstractions.Content;

/// <summary>A run of character levels that each contribute the same adventure-group points.</summary>
public sealed record CharacterManagePointBand(int MinLevel, int MaxLevel, int Point);

/// <summary>The adventure-group (冒险团) tables, as <c>etc/linksystem/charactermanage.etc</c> declares them.</summary>
/// <param name="PointBands"><c>[point bonus]</c>: what each character level contributes.</param>
/// <param name="LevelThresholds">
/// <c>[manage level point]</c>: the points each adventure-group level needs, level one first.
/// </param>
/// <param name="MaxLevel"><c>[manage level max]</c>: the highest adventure-group level.</param>
/// <param name="ExperienceBonusPercent"><c>[exp bonus]</c>: clear experience bonus per adventure-group level.</param>
/// <param name="GoldBonusPercent"><c>[gold bonus]</c>: empty in A21, read so the file is read whole.</param>
public sealed record CharacterManageTable(
    IReadOnlyList<CharacterManagePointBand> PointBands,
    IReadOnlyList<int> LevelThresholds,
    int MaxLevel,
    IReadOnlyDictionary<int, int> ExperienceBonusPercent,
    IReadOnlyDictionary<int, int> GoldBonusPercent);

/// <summary>Reads the adventure-group tables out of the archive.</summary>
/// <remarks>
/// Only the adventure-group rule service reads this. The rule of how the tables combine -- summing,
/// the cap, which character is excluded from the bonus -- lives there, not here.
/// </remarks>
public interface ICharacterManageContentSource
{
    CharacterManageTable Table { get; }
}
