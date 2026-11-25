using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpTotalMatchTeamDomainService(
    IPvpTotalMatchTeamDomainStore store,
    ICharacterRosterDomainService characters,
    ISelectedSessionDomainService sessions,
    IPvpRoomQueryDomainService rooms,
    ITownAreaDomainService town,
    PvpTotalMatchTeamRuleService rules) : IPvpTotalMatchTeamDomainService
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
        if (rules.ReadName(name) is not { } valid) return PvpTotalMatchTeamRefusal.InvalidName;
        return await store.IsNameAvailableAsync(session.AccountId, valid, cancellationToken).ConfigureAwait(false)
            ? PvpTotalMatchTeamRefusal.None : PvpTotalMatchTeamRefusal.NameTaken;
    }

    public async ValueTask<(PvpTotalMatchTeamView? Team, PvpTotalMatchTeamRefusal? Refusal)> SaveAsync(
        SelectedSessionIdentity session, byte[] name, ImmutableArray<byte> positions, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpTotalMatchTeamDomainService) + "." + nameof(SaveAsync));
        if (!CanConfigure(session)) return (null, PvpTotalMatchTeamRefusal.NotAvailable);
        if (rules.ReadName(name) is not { } valid) return (null, PvpTotalMatchTeamRefusal.InvalidName);
        if (!rules.HasValidPositions(positions))
            return (null, PvpTotalMatchTeamRefusal.InvalidComposition);

        var roster = await characters.GetAccountRosterAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (rules.SelectMembers(positions, roster.Characters, session.CharacterId) is not { } members)
            return (null, PvpTotalMatchTeamRefusal.InvalidComposition);

        var existing = await store.ReadAsync(session.AccountId, cancellationToken).ConfigureAwait(false);
        if (rules.NameToSave(valid, existing?.Name) is not { } teamName)
            return (null, PvpTotalMatchTeamRefusal.InvalidName);
        if (!await store.IsNameAvailableAsync(session.AccountId, valid, cancellationToken).ConfigureAwait(false))
            return (null, PvpTotalMatchTeamRefusal.NameTaken);

        var team = new PvpTotalMatchTeam(teamName, [.. members.Select(member => member.CharacterId)]);
        await store.SaveAsync(session.AccountId, team, cancellationToken).ConfigureAwait(false);
        return (new PvpTotalMatchTeamView(team.Name, positions, team.Members[0]), null);
    }

    private bool CanConfigure(SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session)) return false;
        return rooms.FindSeated(session.CharacterId) is { } room
            ? rules.CanConfigureInRoom(session, room)
            : town.Find(session.CharacterId) is not null;
    }
}
