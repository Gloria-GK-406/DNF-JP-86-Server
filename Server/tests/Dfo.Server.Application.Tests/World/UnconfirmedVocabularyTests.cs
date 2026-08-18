using System.Reflection;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Guards the boundary between what the archive and the legacy code actually establish and
/// what a plausible-sounding name would merely imply.
/// </summary>
public sealed class UnconfirmedVocabularyTests
{
    /// <summary>
    /// Names for protocol slots whose meaning is not established.
    /// </summary>
    /// <remarks>
    /// Migration matrix rows 34 and 49 through 53. Each is either a fixed placeholder with
    /// no loaded source or a persisted field whose legacy name is an admission of ignorance.
    /// A member named after one of them would assert a meaning nobody has confirmed, and
    /// would read to a later maintainer as settled fact.
    /// </remarks>
    private static readonly string[] UnconfirmedNames =
    [
        "BossTower",
        "Schedule",
        "PcRoom",
        "PlayTime",
        "Unknown725",
        "Unknown730",
    ];

    /// <summary>
    /// Names for protocol constants, which belong to the projection rather than here.
    /// </summary>
    /// <remarks>
    /// Migration matrix rows 17, 31, 53 and 56. The legacy server emits each unconditionally
    /// from a literal. Carrying one as business state would invent a source for it and
    /// invite someone to start loading it.
    /// </remarks>
    private static readonly string[] ConstantOnlyNames =
    [
        "EventInfo",
        "EventList",
        "ShowEffect",
        "FriendInfo",
        "FriendList",
    ];

    public static TheoryData<Type> ComposedStateTypes =>
    [
        typeof(WorldContentEntryState),
        typeof(SessionAdjacentEntryState),
        typeof(ChannelEntryPolicyState),
        typeof(TownPresenceEntryState),
        typeof(ExpertStoreEntryState),
        typeof(PvpLobbyEntryState),
    ];

    [Theory]
    [MemberData(nameof(ComposedStateTypes))]
    public void No_composed_state_member_names_an_unconfirmed_protocol_slot(Type type)
    {
        var offenders = MemberNames(type)
            .Where(name => UnconfirmedNames.Any(
                forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{type.Name} names unconfirmed protocol slots: {string.Join(", ", offenders)}. "
            + "See Docs/Refactor/SelectCharacterWorldEntryCoverage.md.");
    }

    [Theory]
    [MemberData(nameof(ComposedStateTypes))]
    public void No_composed_state_member_carries_a_protocol_constant(Type type)
    {
        var offenders = MemberNames(type)
            .Where(name => ConstantOnlyNames.Any(
                forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{type.Name} carries protocol constants: {string.Join(", ", offenders)}. "
            + "Those are emitted unconditionally and belong to the projection.");
    }

    private static IEnumerable<string> MemberNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(member => member.Name);
}
