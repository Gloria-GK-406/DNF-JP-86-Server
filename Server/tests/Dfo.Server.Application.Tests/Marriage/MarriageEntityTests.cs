using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageEntityTests
{
    [Fact]
    public async Task The_three_marriage_faces_resolve_to_one_instance()
    {
        await using var w = await ChurchTownAsync(); var f = w.F;
        var query = f.Services.GetRequiredService<IMarriageQueryDomainService>();
        Assert.Same(query, f.Services.GetRequiredService<IMarriageCoupleDomainService>());
        Assert.Same(query, f.Services.GetRequiredService<IMarriageVenueDomainService>());
    }

    [Fact]
    public async Task A_relation_that_moved_after_its_child_record_changed_rolls_the_whole_use_case_back()
    {
        await using var w = await ChurchTownAsync(Tier0(), StaleRelations.Install); var f = w.F; var peer = w.Peer;
        var relations = f.Services.GetRequiredService<StaleRelations>();
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);

        relations.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTx(f, () => Couple(f).RespondProposalAsync(peer, f.Session, 0, default)));
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));
        Assert.Equal(0, f.Scalar("SELECT stage FROM marriage_relations"));
        Assert.Equal(1, f.Scalar("SELECT revision FROM marriage_relations"));

        relations.Armed = false;
        await EligibleAsync(w, f.Session, peer);
        Assert.Equal(1, f.Scalar("SELECT stage FROM marriage_relations"));
        var engaged = f.Scalar("SELECT revision FROM marriage_relations");
        relations.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default)));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM marriage_ceremonies"));
        Assert.Equal(1, f.Scalar("SELECT stage FROM marriage_relations"));
        Assert.Equal(engaged, f.Scalar("SELECT revision FROM marriage_relations"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_relations WHERE ceremony_id IS NULL"));
    }

    /// <summary>
    /// The same refusal reached through the command bus and the 03F1 handler: the transaction rolls back, the handler lets the
    /// exception out without replying to either side, and the account leases are released for the next attempt. At the
    /// socket that exception closes the connection (<c>HandlerFailureConnectionIntegrationTests</c>).
    /// </summary>
    [Fact]
    public async Task A_refused_relation_commit_rolls_the_command_back_and_the_handler_throws_without_replying()
    {
        var sender = new RecordingSender();
        await using var w = await ChurchTownAsync(Tier0(), s => { StaleRelations.Install(s); s.AddSingleton<IGameMessageSender>(sender); s.AddSingleton<Dfo.Server.Abstractions.Content.IQuestDefinitionSource, NoQuests>(); });
        var f = w.F; var peer = w.Peer;
        var relations = f.Services.GetRequiredService<StaleRelations>();
        Assert.True((await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Succeeded);

        relations.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Services.GetRequiredService<ICommandBus>().SendAsync(new MarriageProposalCommand(peer, f.Session, 0), default).AsTask());
        AssertStored(f, stage: 0, revision: 1);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));

        var handler = (IClientMessageHandler)ActivatorUtilities.CreateInstance(f.Services,
            typeof(WeddingResponseRequest).Assembly.GetType("Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler")!);
        var context = new NetworkDispatchContext(new(peer.ConnectionId, NetworkEndpointKind.Game, 10011));
        var accept = new WeddingResponseRequest(GameMessageMetadata.Empty, 0, 1, 0, 0, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(context, accept, default).AsTask());
        Assert.Empty(sender.Deliveries);
        AssertStored(f, stage: 0, revision: 1);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));

        relations.Armed = false;
        await handler.HandleAsync(context, accept, default);
        Assert.Equal(0, Assert.IsType<WeddingResponseAck>(Assert.Single(sender.Deliveries).RequiredDirectMessages[0]).Status);
        AssertStored(f, stage: 1, revision: 2);
    }

    private sealed class NoQuests : Dfo.Server.Abstractions.Content.IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<Dfo.Server.Abstractions.Content.QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<Dfo.Server.Abstractions.Content.QuestDefinition>>([]);
        public ValueTask<Dfo.Server.Abstractions.Content.QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) => ValueTask.FromResult<Dfo.Server.Abstractions.Content.QuestDefinition?>(null);
        public ValueTask<IReadOnlyList<Dfo.Server.Abstractions.Content.TrainingQuestAvailability>> GetTrainingAvailabilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<Dfo.Server.Abstractions.Content.TrainingQuestAvailability>>([]);
    }

    [Fact]
    public async Task The_view_shows_a_lapsed_proposal_as_closed_and_leaves_it_stored_as_proposed()
    {
        await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
        var id = (await InTx(f, () => Couple(f).ProposeAsync(f.Session, peer, default))).Proposal!.Id;
        var live = (await Query(f).ViewAsync(peer, default))!;
        Assert.Equal((id, MarriageStage.Proposed, 1L), (live.RelationId, live.Stage, live.Revision));
        Assert.Equal(f.Session, live.Partner);

        w.Clock.Now += TimeSpan.FromHours(1);
        Assert.Equal(MarriageStage.Closed, (await Query(f).ViewAsync(f.Session, default))!.Stage);
        AssertStored(f, stage: 0, revision: 1);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_proposals"));

        w.Clock.Now -= TimeSpan.FromHours(1);
        Assert.Equal(MarriageStage.Proposed, (await Query(f).ViewAsync(f.Session, default))!.Stage);
        GoOffline(f, peer);
        Assert.Equal(MarriageStage.Closed, (await Query(f).ViewAsync(f.Session, default))!.Stage);
        AssertStored(f, stage: 0, revision: 1);
        Assert.Null(await Query(f).ViewAsync(f.Session with { AccountId = peer.AccountId }, default));
    }

    [Fact]
    public async Task The_view_shows_a_lapsed_reservation_as_engaged_and_leaves_it_stored_as_requested()
    {
        await using var w = await ChurchTownAsync(Tier0()); var f = w.F; var peer = w.Peer;
        await EligibleAsync(w, f.Session, peer);
        var ceremony = (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Ceremony!;
        var live = (await Query(f).ViewAsync(f.Session, default))!;
        Assert.Equal((MarriageStage.Requested, ceremony.Id, true), (live.Stage, live.CeremonyId, live.TrialsCleared));
        var revision = f.Scalar("SELECT revision FROM marriage_relations");

        w.Clock.Now += TimeSpan.FromHours(1);
        var lapsed = (await Query(f).ViewAsync(peer, default))!;
        Assert.Equal((MarriageStage.Engaged, (Guid?)null, (long)revision), (lapsed.Stage, lapsed.CeremonyId, lapsed.Revision));
        AssertStored(f, stage: 2, revision: revision);
        Assert.Equal(0, f.Scalar("SELECT state FROM marriage_ceremonies"));

        w.Clock.Now -= TimeSpan.FromHours(1);
        GoOffline(f, peer);
        Assert.Equal(MarriageStage.Engaged, (await Query(f).ViewAsync(f.Session, default))!.Stage);
        AssertStored(f, stage: 2, revision: revision);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_relations WHERE ceremony_id IS NOT NULL"));
    }

    private static void AssertStored(LearningFixture f, int stage, int revision)
    {
        Assert.Equal(stage, f.Scalar("SELECT stage FROM marriage_relations"));
        Assert.Equal(revision, f.Scalar("SELECT revision FROM marriage_relations"));
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<GameExplicitMessageDelivery> Deliveries { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct)
        { Deliveries.Add(delivery); return ValueTask.CompletedTask; }
    }

    /// <summary>Hands the store a revision one ahead of the caller's, as if another writer had committed in between.</summary>
    private sealed class StaleRelations(IMarriageRelationDomainStore inner) : IMarriageRelationDomainStore
    {
        public bool Armed { get; set; }

        public static void Install(IServiceCollection services)
        {
            var original = services.Last(x => x.ServiceType == typeof(IMarriageRelationDomainStore));
            services.Remove(original);
            services.AddSingleton(original.ImplementationType!);
            services.AddSingleton(sp => new StaleRelations((IMarriageRelationDomainStore)sp.GetRequiredService(original.ImplementationType!)));
            services.AddSingleton<IMarriageRelationDomainStore>(sp => sp.GetRequiredService<StaleRelations>());
        }

        public ValueTask<MarriageRelation?> FindActiveAsync(CharacterId character, CancellationToken ct) => inner.FindActiveAsync(character, ct);
        public ValueTask<MarriageRelation?> ReadAsync(Guid relation, CancellationToken ct) => inner.ReadAsync(relation, ct);
        public ValueTask CreateAsync(Guid relation, MarriageStage stage, DateTimeOffset at, CancellationToken ct) => inner.CreateAsync(relation, stage, at, ct);
        public ValueTask<bool> TransitionAsync(MarriageRelation expected, MarriageStage stage, Guid? ceremony,
            DateTimeOffset at, DateTimeOffset? marriedAt, CancellationToken ct) =>
            inner.TransitionAsync(Armed ? expected with { Revision = expected.Revision + 1 } : expected, stage, ceremony, at, marriedAt, ct);
        public ValueTask<bool> AddLovePointsAsync(Guid relation, int amount, int maximum, CancellationToken ct) =>
            inner.AddLovePointsAsync(relation, amount, maximum, ct);
        public ValueTask<int> CountUnsettledReservationsAsync(CancellationToken ct) => inner.CountUnsettledReservationsAsync(ct);
    }
}
