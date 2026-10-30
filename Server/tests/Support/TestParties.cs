using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Testing;

/// <summary>Real party service and the dungeon run service that reads it, for tests.</summary>
/// <remarks>
/// A dungeon run's members are answered by the party service, so every test run service is built
/// with one. Runs built here remember their party service: the seedless entry helpers in
/// <c>DungeonRunTestEntryExtensions</c> form a missing multi-member party from the entry roster,
/// and departure fixtures can leave the party through <see cref="Leave"/>.
/// </remarks>
internal static class TestParties
{
    private static readonly PartyDirectoryScope Town = new(11, 1, 1);
    private static readonly ConditionalWeakTable<IDungeonRunDomainService, IPartyService> PartiesOfRuns = new();

    /// <summary>A real party service reading where members stand from <paramref name="areas"/>.</summary>
    public static PartyService Create(ITownAreaDomainService? areas = null) => new(
        new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
        TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance,
        areas ?? new OnlineCharacterLocationDomainService());

    /// <summary>A run service over <paramref name="parties"/>, or over a fresh real party service.</summary>
    /// <remarks>A missing transaction accessor keeps the run service's immediate-apply path.</remarks>
    public static DungeonRunDomainService Runs(
        IDomainTransactionAccessor? transactions = null,
        IPartyService? parties = null,
        IWeaknessDomainService? weakness = null,
        ICreatureRuntimeDomainService? creatures = null,
        IDungeonLifecycleDomainService? lifecycle = null,
        TimeProvider? clock = null,
        IDungeonRoomMechanismRuleService? mechanismRules = null,
        IDungeonDeadlineWake? deadlines = null,
        ISealedTreasureRuleService? sealedTreasureRules = null,
        IDungeonParticipationDomainService? participation = null,
        IGiantDragonRuleService? giantDragons = null)
    {
        parties ??= Create();
        var runs = new DungeonRunDomainService(
            transactions!, parties, new LostLandFeverRuleService(), weakness, creatures, lifecycle, clock,
            mechanismRules, deadlines, sealedTreasureRules, participation, giantDragons: giantDragons);
        PartiesOfRuns.AddOrUpdate(runs, parties);
        return runs;
    }

    /// <summary>The party service a run service from <see cref="Runs"/> reads.</summary>
    public static IPartyService Of(IDungeonRunDomainService runs) =>
        PartiesOfRuns.TryGetValue(runs, out var parties)
            ? parties
            : throw new InvalidOperationException("The run service was not built by TestParties.Runs.");

    /// <summary>
    /// Forms the roster's real party unless it is a single entrant (a virtual party) or its leader
    /// already stands in a real party. Only runs built by <see cref="Runs"/> are known.
    /// </summary>
    public static void EnsureEntryParty(
        IDungeonRunDomainService runs, IReadOnlyList<DungeonRunParticipant> roster)
    {
        if (roster.Count < 2 || !PartiesOfRuns.TryGetValue(runs, out var parties))
            return;
        var leader = roster.Single(participant => participant.IsLeader);
        if (parties.KeyOf(leader.CharacterId).Kind == PartyKeyKind.Real)
            return;
        Form(parties, roster);
    }

    /// <summary>
    /// The roster's leader invites the other entrants in slot order and each accepts; the party
    /// assigns slots in that order, so a roster numbered from the leader's slot 0 keeps its slots.
    /// </summary>
    public static PartyKey Form(IPartyService parties, IReadOnlyList<DungeonRunParticipant> roster)
    {
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(roster);
        var leader = roster.Single(participant => participant.IsLeader);
        var inviter = Snapshot(leader);
        foreach (var member in roster.Where(participant => !participant.IsLeader)
                     .OrderBy(participant => participant.Slot))
        {
            var invitee = Snapshot(member);
            var invited = parties.RequestInvite(inviter, invitee, new PartyInviteDisplayState(0, 0, 0, 0));
            if (!invited.Succeeded)
                throw new InvalidOperationException(
                    $"Test party invite of {member.CharacterId} failed: {invited.Failure}.");
            var accepted = parties.AcceptInvite(invitee, inviter);
            if (!accepted.Succeeded)
                throw new InvalidOperationException(
                    $"Test party accept of {member.CharacterId} failed: {accepted.Failure}.");
        }

        var key = parties.KeyOf(leader.CharacterId);
        var formed = parties.FindByKey(key)
            ?? throw new InvalidOperationException("The test party was not formed.");
        if (formed.LeaderCharacterId != leader.CharacterId
            || !formed.Members.Select(member => member.CharacterId).ToHashSet()
                .SetEquals(roster.Select(participant => participant.CharacterId)))
            throw new InvalidOperationException("The test party does not match the entry roster.");
        // The run reads slots from the party, so a roster whose slots the party did not assign would drift silently.
        var slots = formed.Members.ToDictionary(member => member.CharacterId, member => member.Slot);
        if (roster.Any(participant => slots[participant.CharacterId] != participant.Slot))
            throw new InvalidOperationException("The test party assigned slots that differ from the entry roster.");
        return key;
    }

    /// <summary>The character leaves the real party read by <paramref name="runs"/>.</summary>
    public static void Leave(IDungeonRunDomainService runs, CharacterId characterId)
    {
        var left = Of(runs).LeaveParty(characterId);
        if (!left.Succeeded)
            throw new InvalidOperationException($"Test party leave of {characterId} failed: {left.Failure}.");
    }

    private static PartyCharacterSnapshot Snapshot(DungeonRunParticipant participant) => new(
        participant.AccountId, participant.CharacterId,
        unchecked((ushort)participant.CharacterId.Value), Town);
}

/// <summary>Lost Land views of a run's mode state, for assertions.</summary>
public static class LostLandRunStateViews
{
    public static LostLandFeverState? LostLandFever(this DungeonRunState run) => (run.ModeState as DungeonRunModeState.LostLand)?.Fever;
    public static LostLandExplorationState? LostLandExploration(this DungeonRunState run) =>
        (run.ModeState as DungeonRunModeState.LostLand)?.Exploration;
}
