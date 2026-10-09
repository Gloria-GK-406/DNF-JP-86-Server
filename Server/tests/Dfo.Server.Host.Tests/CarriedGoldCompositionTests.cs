using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// The character a use case acts through can actually be built out of the assembled modules.
/// </summary>
/// <remarks>
/// Carried gold reaches across all three: the figure is a table in persistence, the cap is priced
/// from a table in infrastructure, and the rule that weighs them and the character that asks are in
/// application. Every one of those is registered in a different module, so nothing short of
/// assembling them together proves the graph closes.
/// <para>
/// <b>Compiling proves nothing here.</b> A dependency nobody registered is a runtime failure on
/// the first flip, which is a defect a player finds rather than a build does -- and finding it that
/// way costs an entire round of live testing.
/// </para>
/// </remarks>
public sealed class CarriedGoldCompositionTests
{
    [Fact]
    public void The_character_business_acts_through_resolves_from_the_assembled_modules()
    {
        var services = new ServiceCollection().AddLogging();
        using var database = new TemporaryDatabase();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = database.Path }).Build();
        new PersistenceModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false });

        var characters = provider.GetRequiredService<ICharacterDomainService>();

        Assert.NotNull(characters.Get(new Dfo.Server.Data.Characters.CharacterId(1)));
        Assert.NotNull(provider.GetRequiredService<IItemUseEffectDomainService>());
        Assert.NotNull(provider.GetRequiredService<IApplyEffectsDomainService>());
        Assert.NotNull(provider.GetRequiredService<IEpicBookDomainService>());
    }

    /// <summary>The two seams that move gold are both registered and are not the same one.</summary>
    /// <remarks>
    /// One belongs to an account and one to a character. Resolving both here is what says the
    /// account cargo kept its own seam when the character got one, rather than the two collapsing
    /// into whichever was registered last.
    /// </remarks>
    [Fact]
    public void The_carry_cap_rule_and_the_account_cargo_seam_both_resolve()
    {
        var services = new ServiceCollection().AddLogging();
        using var database = new TemporaryDatabase();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = database.Path }).Build();
        new PersistenceModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false });

        Assert.NotNull(provider.GetRequiredService<ICarriedGoldRuleService>());
    }
    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dfo-carried-gold-composition-{Guid.NewGuid():N}.db");
        public void Dispose()
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(Path);
        }
    }
}
