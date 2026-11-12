using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Relations;

/// <summary>The production modules over a throwaway database, with a clock the test moves.</summary>
/// <remarks>
/// Built from the real modules rather than by hand so that what is tested is the registered
/// relation service, its real store and the real identity read, not a copy assembled for the test.
/// </remarks>
internal sealed class RelationFixture : IAsyncDisposable
{
    public static readonly CharacterId A = new(71), B = new(72), C = new(73), Deleted = new(74), E = new(75);

    public string Path { get; }
    public ServiceProvider Services { get; }
    public ManualClock Clock { get; } = new();
    public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();
    public ICharacterRelationDomainService Relations => Services.GetRequiredService<ICharacterRelationDomainService>();
    public ICharacterRelationQueryDomainService Query => Services.GetRequiredService<ICharacterRelationQueryDomainService>();
    public ICharacterIdentityQueryDomainService Identities => Services.GetRequiredService<ICharacterIdentityQueryDomainService>();
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path }.ToString();

    public RelationFixture()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dfo-relations-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = Path }).Build();
        var services = new ServiceCollection().AddLogging();
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        services.AddSingleton<TimeProvider>(Clock);
        Services = services.BuildServiceProvider();
        _ = Services.GetRequiredService<SqliteDatabase>();
        Sql("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'one'),(2,'two'),(3,'three');
            INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,exp,delete_flag) VALUES
                (71,1,'alpha',X'616C706861',0,0,10,0,0),
                (72,2,'beta',X'62657461',1,1,20,0,0),
                (73,3,'gamma',X'67616D6D61',2,0,30,0,0),
                (74,1,'ghost',X'67686F7374',3,0,40,0,1),
                (75,2,'epsilon',X'657073696C6F6E',4,2,50,0,0);
            """);
    }

    /// <summary>Live characters 1001 onwards, for filling a list up to its limit.</summary>
    public IReadOnlyList<CharacterId> AddLiveCharacters(int count)
    {
        var values = string.Join(',', Enumerable.Range(1001, count).Select(id => $"({id},3,'filler{id}',0,0,1,0)"));
        Sql($"INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp) VALUES {values};");
        return Enumerable.Range(1001, count).Select(id => new CharacterId(id)).ToArray();
    }

    /// <summary>Rows written straight to the table, bypassing every rule, to stage a state.</summary>
    public void Relate(CharacterId owner, IEnumerable<CharacterId> targets, CharacterRelationKind kind) =>
        Sql("INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES "
            + string.Join(',', targets.Select(t => $"({owner.Value},{t.Value},{(int)kind},1)")) + ";");

    /// <summary>Runs one writable use case and commits it.</summary>
    public Task<T> WriteAsync<T>(Func<ICharacterRelationDomainService, Task<T>> body) =>
        TestGameplay.RunAsync(Services, async () =>
        {
            await using var transaction = Scope.Begin();
            var result = await body(Relations);
            await transaction.CommitAsync(default);
            return result;
        });

    public Task<CharacterRelationAddOutcome> AddAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind) =>
        WriteAsync(relations => relations.AddAsync(owner, target, kind, default).AsTask());

    public Task<CharacterRelationRemoveOutcome> RemoveAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind) =>
        WriteAsync(relations => relations.RemoveAsync(owner, target, kind, default).AsTask());

    public async Task<CharacterId[]> TargetsAsync(CharacterId owner, CharacterRelationKind kind) =>
        (await Query.ListAsync(owner, kind, default)).Select(x => x.Target).ToArray();

    public int Rows(string where = "1") => Scalar($"SELECT COUNT(*) FROM character_relations WHERE {where}");

    public int Scalar(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Sql(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        using (var connection = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(connection);
        try { File.Delete(Path); }
        catch (IOException) { /* A file the runtime still holds is left for the temp directory to reap. */ }
    }

    internal sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-12T10:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
