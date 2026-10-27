using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>
/// The one place characterization tests obtain marriage services and prepared worlds from.
/// </summary>
/// <remarks>
/// Replacing the marriage service interfaces should only rewire the accessors here; the tests
/// that use them describe behaviour and must keep their assertions unchanged.
/// </remarks>
internal static class MarriageHarness
{
    internal sealed record World(LearningFixture F, SelectedSessionIdentity Peer, RecordingQuestLogs Logs,
        MarriageProposalTests.Clock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    internal sealed class Content(MarriageCeremonyContent value) : IMarriageCeremonyContentSource
    {
        public MarriageCeremonyContent Load() => value;
    }

    internal static MarriageCeremonyContent Tier0(int invitationLimit = 10) =>
        new([new MarriageCeremonyTier(0, 500_000, 0, invitationLimit)], [300, 600, 900])
        { CeremonyPositions = [new(670, 330, 0), new(685, 360, 0)] };

    internal static IMarriageQueryDomainService Query(LearningFixture f) => f.Services.GetRequiredService<IMarriageQueryDomainService>();
    internal static IMarriageCoupleDomainService Couple(LearningFixture f) => f.Services.GetRequiredService<IMarriageCoupleDomainService>();
    internal static IMarriageVenueDomainService Venue(LearningFixture f) => f.Services.GetRequiredService<IMarriageVenueDomainService>();
    internal static IMarriageQuestDomainService WeddingQuests(LearningFixture f) => f.Services.GetRequiredService<IMarriageQuestDomainService>();
    internal static IDomainEventSubscriber<DungeonMapCleared> TrialClears(LearningFixture f) =>
        ActivatorUtilities.CreateInstance<MarriageTrialClearSubscriber>(f.Services);

    internal static async Task<T> InTx<T>(LearningFixture f, Func<ValueTask<T>> action, bool commit = true)
    {
        return await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            var result = await action();
            if (commit) await tx.CommitAsync(default);
            return result;
        });
    }

    private static readonly MethodInfo WeddingInfo = typeof(WeddingProposalRequest).Assembly
        .GetType("Dfo.Server.Protocol.Game.MarriageProposal.MarriageEngagementPayload", throwOnError: true)!
        .GetMethod("Info", BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>The 15-byte 03D0 body the protocol projects for this character today.</summary>
    internal static async Task<byte[]> WeddingInfoAsync(LearningFixture f, AccountId account, CharacterId character) =>
        (byte[])WeddingInfo.Invoke(null, [await Query(f).SnapshotAsync(account, character, default)])!;

    /// <summary>Female 71 (fixture session) and male 72 standing in town 2 area 1, channel 1.</summary>
    internal static async Task<World> ChurchTownAsync(MarriageCeremonyContent? content = null,
        Action<IServiceCollection>? configure = null)
    {
        var (f, peer, logs, clock) = await MarriageCeremonyTests.Fixture(s =>
        {
            s.AddSingleton<ITownDefinitionSource, ChurchTestTowns>();
            if (content is not null) s.AddSingleton<IMarriageCeremonyContentSource>(new Content(content));
            configure?.Invoke(s);
        });
        foreach (var actor in new[] { f.Session, peer }) MoveTo(f, actor, 2, 1);
        return new(f, peer, logs, clock);
    }

    internal static void MoveTo(LearningFixture f, SelectedSessionIdentity actor, byte town, byte area, int channel = 1,
        short x = 100, short y = 200)
    {
        TestGameplay.Run(f.Services, () =>
        {
            var moved = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(actor, town, area, x, y);
            f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(actor.CharacterId, actor.TownAreaLease(),
                new(channel, town, area), moved, true);
        });
    }

    internal static void Arrive(LearningFixture f, SelectedSessionIdentity actor, MarriageChurchRoom room) =>
        MoveTo(f, actor, room.TownId, room.AreaId, room.ChannelId, 300, 350);

    /// <summary>
    /// The same connection selects the same character again: the old session is released first, and
    /// the new one carries the generation the connection is at by then.
    /// </summary>
    internal static SelectedSessionIdentity Reselect(LearningFixture f, SelectedSessionIdentity actor) =>
        Dfo.Server.Testing.TestSessions.ReselectOnSameConnection(
            f.Services.GetRequiredService<ISelectedSessionDomainService>(),
            f.Services.GetRequiredService<OnlineStateRegistry>(),
            actor);

    internal static void GoOffline(LearningFixture f, SelectedSessionIdentity actor)
    {
        TestGameplay.Run(f.Services, () =>
        {
            f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(actor);
            f.Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(actor.CharacterId, actor.TownAreaLease(),
                TownAreaDepartureReason.SessionEnded);
        });
    }

    /// <summary>A further level-85 character, online in town 2 area 1 of <paramref name="channel"/>.</summary>
    internal static SelectedSessionIdentity AddCharacter(LearningFixture f, int account, int character, byte job,
        int gold = 0, int channel = 1)
    {
        f.Sql($"""
            INSERT INTO accounts(account_id,m_id) VALUES({account},'acct{account}');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,bonus_sp,bonus_tp,skill_tree_index)
            VALUES({character},{account},'char{character}',{job},0,85,0,0,0,-1);
            UPDATE characters SET exp=(SELECT exp FROM characters WHERE character_id=71) WHERE character_id={character};
            INSERT INTO character_stats(character_id,carried_gold) VALUES({character},{gold});
            """);
        return TestGameplay.Run(f.Services, () =>
        {
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new($"marriage-extra-{character}"), NetworkEndpointKind.Game, 10011);
            var user = registry.RegisterConnection(connection).OnlineUserId;
            registry.Authenticate(user, new(account));
            var session = SelectedSessionIdentity.Create(new(account), new(character), connection.ConnectionId,
                registry.GetRequiredUser(user).Generation, 10011);
            registry.Select(session);
            registry.EnterWorld(session);
            f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
            var position = new CharacterPositionState(2, 1, 100, 200, 0);
            f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(session, position, 100);
            f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(session.CharacterId, session.TownAreaLease(),
                new(channel, 2, 1), position, true));
            return session;
        });
    }

    internal static async Task<Guid> EngageAsync(LearningFixture f, SelectedSessionIdentity proposer, SelectedSessionIdentity recipient)
    {
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(proposer, recipient, default))).Succeeded);
        return (await InTx(f, () => Couple(f).RespondProposalAsync(recipient, proposer, 0, default))).Proposal!.Id;
    }

    /// <summary>Engages a pair, records both wedding quests as cleared and bound, and funds the applicant.</summary>
    internal static async Task<Guid> EligibleAsync(World w, SelectedSessionIdentity applicant, SelectedSessionIdentity partner,
        int applicantGold = 500_000)
    {
        return await TestGameplay.RunAsync(w.F.Services, async () =>
        {
        var f = w.F;
        var relation = await EngageAsync(f, applicant, partner);
        foreach (var character in new[] { applicant.CharacterId, partner.CharacterId })
            w.Logs.Cleared[character.Value] = [new ClearedQuest(20737, 1), new ClearedQuest(20738, 1)];
        await using (var tx = f.Scope.Begin())
        {
            var bindings = f.Services.GetRequiredService<IMarriageQuestDomainStore>();
            foreach (var character in new[] { applicant.CharacterId, partner.CharacterId })
                foreach (var quest in new[] { 20737, 20738 })
                    await bindings.BindAsync(character, quest, relation, DateTimeOffset.UtcNow, default);
            await tx.CommitAsync(default);
        }
        f.Sql($"""
            INSERT INTO character_stats(character_id,carried_gold) VALUES({applicant.CharacterId.Value},{applicantGold})
            ON CONFLICT(character_id) DO UPDATE SET carried_gold=excluded.carried_gold;
            INSERT INTO character_stats(character_id,carried_gold) VALUES({partner.CharacterId.Value},0)
            ON CONFLICT(character_id) DO NOTHING;
            """);
        return relation;
        });
    }

    internal static async Task<MarriageCeremony> ReserveAsync(World w, SelectedSessionIdentity applicant, SelectedSessionIdentity partner)
    {
        var f = w.F;
        await EligibleAsync(w, applicant, partner);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(applicant, partner, 0, 0, default))).Succeeded);
        var reserved = await InTx(f, () => Couple(f).RespondReservationAsync(partner, applicant, 0, default));
        Assert.Equal(MarriageCeremonyState.Reserved, reserved.Ceremony!.State);
        return reserved.Ceremony;
    }

    internal static Task<MarriageCeremony> ReserveAsync(World w) => ReserveAsync(w, w.F.Session, w.Peer);

    /// <summary>Reserves the fixture pair and opens its wedding; both are admitted but still outside the church.</summary>
    internal static async Task<(MarriageCeremony Ceremony, MarriageChurchRoom Room)> PreparedAsync(World w)
    {
        var ceremony = await ReserveAsync(w);
        var entries = await InTx(w.F, () => Couple(w.F).OpenWeddingAsync(ceremony, default));
        Assert.Equal(2, entries.Count);
        return (ceremony, entries[0].Church.Room);
    }

    /// <summary>Invites <paramref name="guest"/> on behalf of <paramref name="inviter"/> and answers the context key.</summary>
    internal static async Task<uint> InviteAsync(LearningFixture f, SelectedSessionIdentity inviter, SelectedSessionIdentity guest)
    {
        var sent = await InTx(f, () => Venue(f).InviteAsync(inviter, 0, checked((uint)guest.CharacterId.Value), default));
        Assert.Equal(0, sent.Status);
        return Assert.Single(sent.Letters).Key;
    }

    internal static async Task PaidGuestAsync(LearningFixture f, SelectedSessionIdentity guest, uint key, int gold,
        MarriageChurchRoom? enter = null)
    {
        Assert.Equal(0, await InTx(f, () => Venue(f).GiftAsync(guest, key, gold, default)));
        if (enter is not null)
            Assert.Equal(0, (await InTx(f, () => Venue(f).EnterChurchAsync(guest, key, enter.AreaId, 1, default))).Status);
    }
}
