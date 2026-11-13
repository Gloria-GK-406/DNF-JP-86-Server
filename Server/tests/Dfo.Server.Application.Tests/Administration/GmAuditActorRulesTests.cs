using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Administration;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Tests.Administration;

/// <summary>The one audit write path keeps the actor and its account together (#329, RQ-024).</summary>
public sealed class GmAuditActorRulesTests
{
    private static GmAuditRecord Record(string action = "GrantCera", long? administratorId = 1, string? name = "operator",
        string outcome = "Succeeded", string actor = GmAuditActors.Gm, int? accountId = null) =>
        new("operation", DateTimeOffset.UnixEpoch, action, administratorId, name, "target", outcome, "reason", actor, accountId);

    public static TheoryData<string, GmAuditRecord> Accepted => new()
    {
        { "administrator", Record() },
        { "administrator login refused for an unknown name", Record("Login", null, null, "Rejected") },
        { "administrator player-portal action", Record("ChangePlayerPortal") },
        { "player", Record("PlayerLogout", null, "alice", actor: GmAuditActors.Player, accountId: 7) },
        { "player login refused, known account", Record("PlayerLogin", null, "alice", "Rejected", GmAuditActors.Player, 7) },
        { "player login refused, no account", Record("PlayerLogin", null, GmAuditActors.UnrecordedPlayerName, "Rejected", GmAuditActors.Player) },
    };

    public static TheoryData<string, GmAuditRecord> Refused => new()
    {
        { "player without account", Record("PlayerLogout", null, "alice", actor: GmAuditActors.Player) },
        { "player with administrator", Record("PlayerLogout", 1, "alice", actor: GmAuditActors.Player, accountId: 7) },
        { "player without name", Record("PlayerLogout", null, null, actor: GmAuditActors.Player, accountId: 7) },
        { "player with administrator action", Record("GrantCera", null, "alice", actor: GmAuditActors.Player, accountId: 7) },
        { "player with zero account", Record("PlayerLogout", null, "alice", actor: GmAuditActors.Player, accountId: 0) },
        { "succeeded player login without account", Record("PlayerLogin", null, GmAuditActors.UnrecordedPlayerName, "Succeeded", GmAuditActors.Player) },
        { "refused login without account naming someone", Record("PlayerLogin", null, "no-such-account", "Rejected", GmAuditActors.Player) },
        { "administrator with account", Record(accountId: 7) },
        { "administrator with player action", Record("PlayerLogin") },
        { "unknown actor", Record(actor: "robot") },
    };

    [Theory, MemberData(nameof(Accepted))]
    public async Task Matching_actor_and_account_are_written(string _, GmAuditRecord record)
    {
        var store = new Store();
        await new GmAuditDomainService(store).AppendAsync(record, CancellationToken.None);
        Assert.Same(record, Assert.Single(store.Appended));
    }

    [Theory, MemberData(nameof(Refused))]
    public async Task Mismatched_actor_and_account_are_refused_before_storage(string _, GmAuditRecord record)
    {
        var store = new Store();
        await Assert.ThrowsAsync<ArgumentException>(() => new GmAuditDomainService(store).AppendAsync(record, CancellationToken.None).AsTask());
        Assert.Empty(store.Appended);
    }

    private sealed class Store : IGmAdministrationPersistence
    {
        internal List<GmAuditRecord> Appended { get; } = [];
        public ValueTask AppendAuditAsync(GmAuditRecord record, CancellationToken cancellationToken) { Appended.Add(record); return ValueTask.CompletedTask; }
        public ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> HasAdministratorAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmAdministrator?> FindAdministratorAsync(string loginName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmAdministrator?> FindAdministratorAsync(long id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask CreateAdministratorAsync(string loginName, string passwordRecord, GmPermission permissions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SaveSessionAsync(GmSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmSession?> FindSessionAsync(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DeleteSessionAsync(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<GmAuditPage> QueryAuditAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
