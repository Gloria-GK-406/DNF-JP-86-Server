using System.Text;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Infrastructure.Tests.Characters;

/// <summary>Persistence for character creation, name lookup and soft deletion.</summary>
public sealed class SqliteCharacterLifecycleTests
{
    private static readonly AccountId Account = new(1);

    private static async Task WithRepositoryAsync(Func<SqliteCharacterRepository, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await body(new SqliteCharacterRepository(Path.Combine(directory, "characters.db")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public Task ACreatedCharacterComesBackOnTheRoster() => WithRepositoryAsync(async repository =>
    {
        var id = await repository.CreateAsync(
            new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0),
            CancellationToken.None);

        var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

        var only = Assert.Single(roster);
        Assert.Equal(id, only.CharacterId);
        Assert.Equal("Nagisa"u8.ToArray(), only.Name);
        Assert.Equal(3, only.Job);
        Assert.Equal(1, only.Level);
        Assert.Equal(0, only.SlotIndex);
    });

    [Fact]
    public Task NameLookupMatchesOnBytesRatherThanDecodedText() =>
        WithRepositoryAsync(async repository =>
        {
            var name = Encoding.UTF8.GetBytes("さくら");
            await repository.CreateAsync(
                new NewCharacterSpec(Account, name, 0, 0),
                CancellationToken.None);

            Assert.True(await repository.NameExistsAsync(name, CancellationToken.None));
            Assert.False(await repository.NameExistsAsync(
                Encoding.UTF8.GetBytes("さくらこ"),
                CancellationToken.None));
        });

    [Fact]
    public Task ANameThatIsNotValidUtf8SurvivesTheRoundTripIntact() =>
        WithRepositoryAsync(async repository =>
        {
            // The display column cannot hold these bytes, so the blob column is the authority.
            // If the read path ever fell back to the text column this would come back mangled.
            byte[] undecodable = [0x41, 0xC3, 0x28, 0x42];
            await repository.CreateAsync(
                new NewCharacterSpec(Account, undecodable, 0, 0),
                CancellationToken.None);

            var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

            Assert.Equal(undecodable, Assert.Single(roster).Name);
            Assert.True(await repository.NameExistsAsync(undecodable, CancellationToken.None));
        });

    [Fact]
    public Task SoftDeleteHidesTheCharacterAndFreesItsName() =>
        WithRepositoryAsync(async repository =>
        {
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), 0, 0),
                CancellationToken.None);

            Assert.True(await repository.SoftDeleteAsync(id, CancellationToken.None));

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));
            Assert.False(await repository.NameExistsAsync("Doomed"u8.ToArray(), CancellationToken.None));
        });

    [Fact]
    public Task DeletingTwiceReportsFailureTheSecondTime() =>
        WithRepositoryAsync(async repository =>
        {
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Once"u8.ToArray(), 0, 0),
                CancellationToken.None);

            Assert.True(await repository.SoftDeleteAsync(id, CancellationToken.None));
            Assert.False(await repository.SoftDeleteAsync(id, CancellationToken.None));
        });

    [Fact]
    public Task TheRosterIsOrderedBySlot() => WithRepositoryAsync(async repository =>
    {
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Third"u8.ToArray(), 0, 2), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "First"u8.ToArray(), 0, 0), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Second"u8.ToArray(), 0, 1), CancellationToken.None);

        var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

        Assert.Equal(
            ["First", "Second", "Third"],
            roster.Select(character => Encoding.UTF8.GetString(character.Name)));
    });

    [Fact]
    public Task AnotherAccountsCharacterIsNotOnThisRosterButStillOwnsItsName() =>
        WithRepositoryAsync(async repository =>
        {
            await repository.CreateAsync(
                new NewCharacterSpec(new AccountId(2), "Theirs"u8.ToArray(), 0, 0),
                CancellationToken.None);

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));

            // Names are unique server-wide, not per account.
            Assert.True(await repository.NameExistsAsync("Theirs"u8.ToArray(), CancellationToken.None));
        });
}
