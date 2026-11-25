using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpTotalMatchTeamDomainService(
    IPvpTotalMatchTeamDomainStore store,
    ICharacterRosterDomainService characters,
    ISelectedSessionDomainService sessions,
    IPvpRoomQueryDomainService rooms,
    ITownAreaDomainService town) : IPvpTotalMatchTeamDomainService
{
    public async ValueTask<PvpTotalMatchTeamView?> ReadAsync(AccountId account, CancellationToken cancellationToken)
    {
        if (await store.ReadAsync(account, cancellationToken).ConfigureAwait(false) is not { } team)
            return null;

        var roster = await characters.GetAccountRosterAsync(account, cancellationToken).ConfigureAwait(false);
        var positions = roster.Characters.Select((character, index) => (character.CharacterId, index))
            .ToDictionary(entry => entry.CharacterId, entry => entry.index);
        var result = ImmutableArray.CreateBuilder<byte>(PvpTotalMatchTeam.MemberCount);
        foreach (var member in team.Members)
        {
            if (!positions.TryGetValue(member, out var index) || index >= byte.MaxValue)
                return null;
            result.Add((byte)index);
        }
        return new PvpTotalMatchTeamView(team.Name, result.MoveToImmutable(), team.Members[0]);
    }

    public async ValueTask<PvpTotalMatchTeamRefusal> CheckNameAsync(
        SelectedSessionIdentity session, byte[] name, CancellationToken cancellationToken)
    {
        if (!CanConfigure(session)) return PvpTotalMatchTeamRefusal.NotAvailable;
        if (ReadName(name) is not { } valid) return PvpTotalMatchTeamRefusal.InvalidName;
        return await store.IsNameAvailableAsync(session.AccountId, valid, cancellationToken).ConfigureAwait(false)
            ? PvpTotalMatchTeamRefusal.None : PvpTotalMatchTeamRefusal.NameTaken;
    }

    public async ValueTask<(PvpTotalMatchTeamView? Team, PvpTotalMatchTeamRefusal? Refusal)> SaveAsync(
        SelectedSessionIdentity session, byte[] name, ImmutableArray<byte> positions, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpTotalMatchTeamDomainService) + "." + nameof(SaveAsync));
        if (!CanConfigure(session)) return (null, PvpTotalMatchTeamRefusal.NotAvailable);
        if (ReadName(name) is not { } valid) return (null, PvpTotalMatchTeamRefusal.InvalidName);
        if (positions.IsDefault || positions.Length != PvpTotalMatchTeam.MemberCount || positions.Distinct().Count() != positions.Length)
            return (null, PvpTotalMatchTeamRefusal.InvalidComposition);

        var roster = await characters.GetAccountRosterAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (positions.Any(position => position == byte.MaxValue || position >= roster.Characters.Count)
            || !roster.Characters.Any(character => character.CharacterId == session.CharacterId))
            return (null, PvpTotalMatchTeamRefusal.InvalidComposition);

        var members = positions.Select(position => roster.Characters[position]).ToArray();
        // The client compares job and advancement. Awakening uses the upper growth-type bits.
        if (members.Select(member => (member.Job, Advancement: member.GrowType & 0xF)).Distinct().Count() != members.Length)
            return (null, PvpTotalMatchTeamRefusal.InvalidComposition);

        var existing = await store.ReadAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing.Name, valid, StringComparison.OrdinalIgnoreCase))
            return (null, PvpTotalMatchTeamRefusal.InvalidName);
        if (!await store.IsNameAvailableAsync(session.AccountId, valid, cancellationToken).ConfigureAwait(false))
            return (null, PvpTotalMatchTeamRefusal.NameTaken);

        var team = new PvpTotalMatchTeam(existing?.Name ?? valid, [.. members.Select(member => member.CharacterId)]);
        await store.SaveAsync(session.AccountId, team, cancellationToken).ConfigureAwait(false);
        return (new PvpTotalMatchTeamView(team.Name, positions, team.Members[0]), null);
    }

    private bool CanConfigure(SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session)) return false;
        if (rooms.FindSeated(session.CharacterId) is { } room)
        {
            return room.Phase == PvpRoomPhase.Waiting
                && !room.IsMatchmaking
                && room.ListenerPort == session.ListenerPort
                && room.Members.Any(member => member.Member.Session == session);
        }
        return town.Find(session.CharacterId) is not null;
    }

    private static string? ReadName(byte[] name)
    {
        if (CharacterNameRules.Inspect(name, 1, PvpTotalMatchTeam.NameByteLimit) != CharacterNameShape.WellFormed)
            return null;
        var text = Encoding.UTF8.GetString(name);
        return text.EnumerateRunes().Count() <= PvpTotalMatchTeam.NameCharacterLimit ? text : null;
    }
}
