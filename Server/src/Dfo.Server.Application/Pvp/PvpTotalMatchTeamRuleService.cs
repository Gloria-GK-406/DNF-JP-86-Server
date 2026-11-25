using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpTotalMatchTeamRuleService
{
    public string? ReadName(byte[] name)
    {
        if (CharacterNameRules.Inspect(name, 1, PvpTotalMatchTeam.NameByteLimit) != CharacterNameShape.WellFormed)
            return null;
        var text = Encoding.UTF8.GetString(name);
        return text.EnumerateRunes().Count() <= PvpTotalMatchTeam.NameCharacterLimit ? text : null;
    }

    public string? NameToSave(string proposed, string? existing) =>
        existing is null || string.Equals(existing, proposed, StringComparison.OrdinalIgnoreCase)
            ? existing ?? proposed : null;

    public bool HasValidPositions(ImmutableArray<byte> positions) =>
        !positions.IsDefault && positions.Length == PvpTotalMatchTeam.MemberCount
        && positions.Distinct().Count() == positions.Length;

    public IReadOnlyList<CharacterSummary>? SelectMembers(ImmutableArray<byte> positions,
        IReadOnlyList<CharacterSummary> roster, CharacterId current)
    {
        if (positions.Any(position => position == byte.MaxValue || position >= roster.Count)
            || !roster.Any(character => character.CharacterId == current))
            return null;

        var members = positions.Select(position => roster[position]).ToArray();
        // A21 compares job and advancement. Awakening uses the upper growth-type bits.
        return members.Select(member => (member.Job, CharacterCareer.FromPacked(member.Job, member.GrowType).GrowType))
            .Distinct().Count() == members.Length ? members : null;
    }

    public bool CanConfigureInRoom(SelectedSessionIdentity session, PvpRoomSnapshot room) =>
        room.Phase == PvpRoomPhase.Waiting && !room.IsMatchmaking && room.ListenerPort == session.ListenerPort
        && room.Members.Any(member => member.Member.Session == session);
}
