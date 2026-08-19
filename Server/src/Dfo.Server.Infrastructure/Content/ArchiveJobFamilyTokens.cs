using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// The archive's spelling of a job family.
/// </summary>
/// <remarks>
/// One table, because the archive uses one vocabulary. A quest's target restriction and an item's
/// <c>[usable job]</c> both draw on these fourteen names, and two copies would drift the first time
/// the archive gains a family.
/// <para>
/// The archive also contains spellings this table does not carry -- <c>[prist]</c> is a typo in
/// five files, and <c>[creator mage]</c>, <c>[account]</c> and <c>[pall]</c> are families the
/// server does not model. They resolve to
/// <see cref="QuestJobRequirement.Unrecognized"/>; each caller decides whether that is a refusal or
/// an error, because the two callers genuinely differ. A quest catalogue is built once at startup
/// and can afford to reject the whole definition; a use request cannot throw at the player.
/// </para>
/// </remarks>
internal static class ArchiveJobFamilyTokens
{
    private static readonly Dictionary<string, QuestJobRequirement> Tokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["[all]"] = QuestJobRequirement.Any,
            ["[swordman]"] = QuestJobRequirement.Swordman,
            ["[fighter]"] = QuestJobRequirement.Fighter,
            ["[gunner]"] = QuestJobRequirement.Gunner,
            ["[mage]"] = QuestJobRequirement.Mage,
            ["[priest]"] = QuestJobRequirement.Priest,
            ["[thief]"] = QuestJobRequirement.Thief,
            ["[knight]"] = QuestJobRequirement.Knight,
            ["[demonic swordman]"] = QuestJobRequirement.DemonicSwordman,
            ["[at swordman]"] = QuestJobRequirement.AwakenedSwordman,
            ["[at fighter]"] = QuestJobRequirement.AwakenedFighter,
            ["[at gunner]"] = QuestJobRequirement.AwakenedGunner,
            ["[at mage]"] = QuestJobRequirement.AwakenedMage,
            ["[at priest]"] = QuestJobRequirement.AwakenedPriest,
            ["[at thief]"] = QuestJobRequirement.AwakenedThief,
            ["[at knight]"] = QuestJobRequirement.AwakenedKnight,
        };

    /// <summary>Every spelling this table carries, for a caller that must scan text for them.</summary>
    public static IReadOnlyDictionary<string, QuestJobRequirement> All => Tokens;

    /// <summary>Whether the archive uses this spelling for a family the server models.</summary>
    public static bool TryResolve(string token, out QuestJobRequirement family) =>
        Tokens.TryGetValue(token, out family);

    /// <summary>The family this spelling names, or Unrecognized.</summary>
    public static QuestJobRequirement Resolve(string token) =>
        Tokens.TryGetValue(token, out var family) ? family : QuestJobRequirement.Unrecognized;
}
