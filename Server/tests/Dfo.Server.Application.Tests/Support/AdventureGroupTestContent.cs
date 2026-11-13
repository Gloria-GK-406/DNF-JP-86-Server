using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// The A21 <c>etc/linksystem/charactermanage.etc</c> tables, transcribed, for fixtures that must not
/// reach the developer's archive. <c>PvfCharacterManageContentSourceTests</c> checks the same
/// figures against the real file.
/// </summary>
internal sealed class AdventureGroupTestContent : ICharacterManageContentSource
{
    internal static readonly int[] Thresholds = [1375, 5425, 10850, 21700, 37975, 54250, 75950, 101575, 133725, 175350];

    public CharacterManageTable Table { get; } = new(
        Enumerable.Range(40, 61).Select(level => new CharacterManagePointBand(
            level, level, level <= 70 ? 100 + 5 * (level - 40) : 260 + 10 * (level - 71))).ToArray(),
        Thresholds,
        10,
        Enumerable.Range(1, 10).ToDictionary(level => level, _ => 10),
        new Dictionary<int, int>());
}
