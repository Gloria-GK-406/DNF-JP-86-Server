using System.Text;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>Persistence for character creation, name lookup and soft deletion.</summary>
public sealed class SqliteCharacterLifecycleTests
{
    /// <summary>What the creation rules stamp every worn piece with.</summary>
    private const byte EquipmentKind = 1;

    private static readonly AccountId Account = new(1);

    private static async Task WithRepositoryAsync(Func<ICharacterRepository, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await body(new CharacterSide(Path.Combine(directory, "characters.db")).PerCall);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public Task ACreatedCharacterCanBeEntered()
    {
        // The entry profile query inner-joins three tables. Creation used to write only the
        // characters row, so every character it produced was rejected at select-character with
        // "incomplete required profile state" -- a corruption assertion firing on a character the
        // server had just built itself.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            // The account loader below reads a real account row, so create one first. Its id is
            // the autoincrement seed, which is the Account this test uses.
            var created = await new AccountSide(databasePath).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);

            var repository = new CharacterSide(databasePath).PerCall;
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            var entryState = new CharacterSide(databasePath).EntryState(new GoldLimitRuleService());
            var record = await entryState.LoadAsync(Account, id, CancellationToken.None);

            Assert.Equal(id, record.CharacterId);
            Assert.Equal(3, record.Profile.Job);
            Assert.Equal(1, record.Profile.Level);

            var coins = Assert.Single(
                await new InventorySide(databasePath).ReadMainItemsAsync(Account, id, CancellationToken.None),
                item => item.SlotIndex == 1);
            Assert.Equal(1, coins.Core.ItemId);
            Assert.Equal(10, coins.Core.Value);

            // Entering also loads account-scoped state, and that side reads this character's
            // rows too. Asserting only the character loader is what let a null pc_room_state
            // through: the character profile never reads that column, while the account loader
            // left-joins it for every live character and refuses a null.
            var account = await new AccountSide(databasePath)
                .Entry.LoadAsync(Account, CancellationToken.None);

            Assert.Equal(Account, account.AccountId);
        });
    }

    [Fact]
    public Task ACreatedCharacterKeepsTheStatsItWasBornWith()
    {
        // Creation used to leave every stat at its schema default, so a character entered the
        // world with zero maximum HP. The values below are the swordman's, scaled the way the
        // archive's numbers are, and they travel back out through the same loader select-character
        // uses rather than through a hand-written query.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("10038", "hash");

            var stats = new CharacterBaseStats(
                HpMax: 1800, MpMax: 1400,
                PhysicalAttack: 75, PhysicalDefense: 75,
                MagicalAttack: 45, MagicalDefense: 45,
                DarkResistance: 200, LightResistance: -200,
                InventoryLimit: 480000, MpRegenSpeed: 500,
                MoveSpeed: 8500, AttackSpeed: 8500, CastSpeed: 7000,
                HitRecovery: 6000, JumpPower: 4300, Weight: 500000);
            var repository = new CharacterSide(databasePath).PerCall;
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 0, SlotIndex: 0, stats),
                CancellationToken.None);

            var stored = (await new CharacterSide(databasePath).EntryState(new GoldLimitRuleService())
                .LoadAsync(Account, id, CancellationToken.None)).Profile.DerivedStats;

            Assert.Equal(stats.HpMax, stored.HpMaximum);
            Assert.Equal(stats.MpMax, stored.MpMaximum);
            Assert.Equal(stats.PhysicalAttack, stored.PhysicalAttack);
            Assert.Equal(stats.MagicalDefense, stored.MagicalDefense);
            Assert.Equal(stats.InventoryLimit, stored.InventoryLimit);
            Assert.Equal(stats.MoveSpeed, stored.MoveSpeed);
            Assert.Equal(stats.Weight, stored.Weight);

            // Signed and absent stats are the two that a column-order slip would silently pass:
            // the swordman's light resistance is negative and he declares no fire resistance.
            Assert.Equal(-200, stored.LightResistance);
            Assert.Equal(200, stored.DarkResistance);
            Assert.Equal(0, stored.FireResistance);

            // Born at full health. The column's schema default is zero, and zero here is worse
            // than a missing stat: the client draws an empty bar under an intact maximum, which
            // is how a character with correct maxima still showed no HP at all.
            Assert.Equal(100, stored.CurrentHpMpPercent);
        });
    }

    [Fact]
    public Task CreationLeavesNoCharacterBehindWhenItCannotComplete()
    {
        // Atomicity is the whole reason the entry-state rows are written here rather than by a
        // second call from the service layer: a character row that outlived a failed follow-up
        // would be exactly the half-built state the assertion exists to catch.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            var repository = new CharacterSide(databasePath).PerCall;
            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE character_settings;";
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await Assert.ThrowsAnyAsync<Exception>(async () => await repository.CreateAsync(
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None));

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));
        });
    }

    /// <summary>
    /// Each character on the roster is drawn wearing its own things, and a bare one wears none.
    /// </summary>
    /// <remarks>
    /// <b>The half of this case that asserted a hand-written blob is gone, and its going is the
    /// point.</b> It used to write a part into <c>characters.appearance_blob</c> for a character
    /// wearing nothing and assert the roster came back carrying it. That assertion was a statement
    /// of the defect: the column is written once when a character is made and by nothing
    /// afterwards, so the screen drew the character the player made rather than the one they have.
    /// The roster derives from what is worn now, and the replacement for that half is
    /// <see cref="ChangingWhatACharacterWearsChangesWhatTheRosterDraws"/>.
    /// <para>
    /// Three characters on one account, because "does not borrow" needs at least two dressed
    /// differently and one wearing nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public Task TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var repository = new CharacterSide(databasePath).PerCall;

            var bare = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Bare"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);
            var clothed = await repository.CreateAsync(
                new NewCharacterSpec(
                    Account, "Clothed"u8.ToArray(), Job: 0, SlotIndex: 2, CharacterBaseStats.Zero,
                    InitialEquipment:
                    [
                        new NewCharacterEquipment(12, 27600, 45, EquipmentKind),
                        new NewCharacterEquipment(14, 10400, 33, EquipmentKind),
                        new NewCharacterEquipment(16, 12400, 27, EquipmentKind),
                    ]),
                CancellationToken.None);
            var other = await repository.CreateAsync(
                new NewCharacterSpec(
                    Account, "Other"u8.ToArray(), Job: 2, SlotIndex: 1, CharacterBaseStats.Zero,
                    InitialEquipment: [new NewCharacterEquipment(0, 413560000, 45, EquipmentKind)]),
                CancellationToken.None);

            var roster = await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None);

            // A character given nothing to wear is drawn wearing nothing rather than a stand-in
            // set: the roster would otherwise draw every character holding whichever job's weapon
            // the stand-in was transcribed from. Empty, not absent -- this listing was asked.
            Assert.Empty(Drawn(roster.Single(value => value.CharacterId == bare)));

            // The coat and the pants are worn but not drawn: the run this screen draws from ends
            // at slot twelve and takes twenty-three as well, and fourteen and sixteen are neither.
            var worn = Drawn(roster.Single(value => value.CharacterId == clothed));
            Assert.Equal([12], worn.Select(value => (int)value.Slot));
            Assert.Equal([27600], worn.Select(value => value.DisplayItemId));
            Assert.Equal([4], worn.Select(value => value.ExpansionLength));

            var others = Drawn(roster.Single(value => value.CharacterId == other));
            Assert.Equal([0], others.Select(value => (int)value.Slot));
            Assert.Equal([413560000], others.Select(value => value.DisplayItemId));
        });
    }

    /// <summary>The roster draws the slots the shared rule names, and no others.</summary>
    /// <remarks>
    /// A worn set straddling every edge of the run: its first slot, its last, the slot one past it
    /// that this repository's creation-time filter would have taken, the extra slot the run reaches
    /// for, and the creature's, which belongs to a different part of the frame that draws a
    /// character in the world. The same expectation is asserted against the in-world frame in
    /// <c>BothScreensDrawTheSameCharacterTests</c>; both sides pin it so that mutating the one rule
    /// cannot leave the two agreeing about a wrong picture.
    /// </remarks>
    [Fact]
    public Task TheRosterDrawsExactlyTheSlotsTheRuleNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var repository = new CharacterSide(databasePath).PerCall;
            await repository.CreateAsync(
                new NewCharacterSpec(
                    Account, "Edges"u8.ToArray(), Job: 0, SlotIndex: 0, CharacterBaseStats.Zero,
                    InitialEquipment:
                    [
                        new NewCharacterEquipment(0, 413560000, 45, EquipmentKind),
                        new NewCharacterEquipment(12, 27600, 45, EquipmentKind),
                        new NewCharacterEquipment(13, 10400, 45, EquipmentKind),
                        new NewCharacterEquipment(23, 12400, 45, EquipmentKind),
                        new NewCharacterEquipment(24, 88800, 45, EquipmentKind),
                    ]),
                CancellationToken.None);

            var drawn = Drawn((await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single());

            Assert.Equal([0, 12, 13, 24], drawn.Select(value => (int)value.Slot));
            Assert.Equal([413560000, 27600, 10400, 88800], drawn.Select(value => value.DisplayItemId));
        });
    }

    /// <summary>
    /// Changing what a character wears changes what the selection screen draws for it.
    /// </summary>
    /// <remarks>
    /// Nothing else happens in between -- no re-entry, no second command, no write to the stored
    /// blob, which is left holding what creation put there. This is the case the reported defect
    /// failed: eleven costumes went on and the selection screen went on drawing the character
    /// undressed.
    /// </remarks>
    [Fact]
    public Task ChangingWhatACharacterWearsChangesWhatTheRosterDraws()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var side = new CharacterSide(databasePath);
            var repository = side.PerCall;
            var character = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Dresser"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.Empty(Drawn((await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single()));

            await Wear(side, character, slot: 3, itemId: 413500000);

            var afterFirst = Drawn((await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single());
            Assert.Equal([3], afterFirst.Select(value => (int)value.Slot));
            Assert.Equal([413500000], afterFirst.Select(value => value.DisplayItemId));

            // The part carries more than the slot and the item. The state byte is the piece's
            // attribute doubled plus one for being amplified, and the last byte is its enchant
            // count -- asserted here, off the store, so that the roster side has its own hold on
            // what a part carries rather than borrowing the frame codec's.
            var part = Assert.Single(afterFirst);
            Assert.Equal(11, part.State);
            Assert.Equal(9, part.Flag20);
            Assert.Equal(4, part.ExpansionLength);
            Assert.Equal(0, part.LinkItemId);
            Assert.Equal(0u, part.EnchantValue);

            await Wear(side, character, slot: 0, itemId: 413560000);

            var afterSecond = Drawn((await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single());
            Assert.Equal([0, 3], afterSecond.Select(value => (int)value.Slot));
            Assert.Equal([413560000, 413500000], afterSecond.Select(value => value.DisplayItemId));

            await TakeOff(side, character, slot: 3);

            var afterRemoval = Drawn((await repository.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single());
            Assert.Equal([0], afterRemoval.Select(value => (int)value.Slot));
            Assert.Equal([413560000], afterRemoval.Select(value => value.DisplayItemId));
        });
    }

    /// <summary>
    /// A character is drawn in what it wears now, not in what it was created wearing.
    /// </summary>
    /// <remarks>
    /// Creation once stored an appearance of its own; the roster frames read worn equipment
    /// instead, so changing it after creation shows on the selection screen.
    /// </remarks>
    [Fact]
    public Task TheRosterDrawsWhatIsWornAfterCreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var side = new CharacterSide(databasePath);
            var character = await side.PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account, "Frozen"u8.ToArray(), Job: 0, SlotIndex: 0, CharacterBaseStats.Zero,
                    InitialEquipment: [new NewCharacterEquipment(12, 27600, 45, EquipmentKind)]),
                CancellationToken.None);

            // Put something else on afterwards; the selection screen follows it.
            await Wear(side, character, slot: 0, itemId: 413560000);

            var drawn = Drawn((await side.PerCall.ListByAccountWithAppearanceAsync(
                Account, CancellationToken.None)).Single());
            Assert.Equal([0, 12], drawn.Select(value => (int)value.Slot));
        });
    }

    /// <summary>
    /// The listings that draw nothing never read what anyone is wearing; the one that draws does.
    /// </summary>
    /// <remarks>
    /// <b>This is the case that keeps the account-scoped worn read on the one path that needs it.</b>
    /// The first version of this work put that read inside
    /// <see cref="ICharacterRepository.ListByAccountAsync"/>, which is also what resolves a roster
    /// position, counts slots before a creation, finds a character by name before a deletion, and
    /// checks during entry that the selected character is on the roster. None of those four draws
    /// anything, and all four would have failed whenever that read failed -- so a database busy for
    /// a moment, or one worn row this server did not write, would have kept a player from creating,
    /// deleting or entering with any character on the account. A screen drawn correctly is not
    /// worth a login that cannot happen.
    /// <para>
    /// The refusal is put at the seam rather than made out of a damaged row, because a damaged row
    /// cannot be made: <c>items.item_core</c> is <c>NOT NULL CHECK (length(item_core) = 82)</c> and
    /// <c>item_uid</c> is bounded to the wire's range, so the table itself refuses the two shapes
    /// the read would reject. What remains reachable is the read failing for its own reasons, and
    /// that is what stands in here. Everything else about the database is real.
    /// </para>
    /// <para>
    /// Folding the read back into the cheap listing turns the first half of this red.
    /// </para>
    /// </remarks>
    [Fact]
    public Task TheListingsThatDrawNothingDoNotReadWhatAnyoneWears()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var side = new CharacterSide(databasePath);
            var repository = side.PerCallOver(new RefusesTheWornRead(side.Items));
            await repository.CreateAsync(
                new NewCharacterSpec(
                    Account, "Dressed"u8.ToArray(), Job: 0, SlotIndex: 0, CharacterBaseStats.Zero,
                    InitialEquipment: [new NewCharacterEquipment(12, 27600, 45, EquipmentKind)]),
                CancellationToken.None);
            await repository.CreateAsync(
                new NewCharacterSpec(Account, "Bystander"u8.ToArray(), Job: 1, SlotIndex: 1, CharacterBaseStats.Zero),
                CancellationToken.None);

            // Creating a character has already worked twice above, through the same repository.
            // Listing, which is what deletion, roster-position resolution and entry all reach for,
            // works too -- and says it worked nothing out about appearances rather than saying
            // everyone is naked.
            var listed = await repository.ListByAccountAsync(Account, CancellationToken.None);
            Assert.Equal(2, listed.Count);
            Assert.All(listed, summary => Assert.Null(summary.Appearances));

            // The one caller that draws is the one that meets the refusal.
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await repository.ListByAccountWithAppearanceAsync(
                    Account, CancellationToken.None));
        });
    }

    /// <summary>A character's things, except that nobody can be told what anyone wears.</summary>
    private sealed class RefusesTheWornRead(ICharacterItemTable inner) : ICharacterItemTable
    {
        public ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            inner.ReadByCharacterAsync(characterId, cancellationToken);

        public ValueTask<IReadOnlyList<WornItemRow>> ReadWornByAccountAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            throw new InvalidDataException("What this account wears cannot be read.");


    }

    /// <summary>The parts one character is drawn as, refusing a listing that worked none out.</summary>
    /// <remarks>
    /// Null and empty are two different answers here: empty is "asked, wears nothing drawn", null is
    /// "this listing never worked it out". Every case above asks the listing that does, so null is a
    /// failure of the case rather than a shape it should cope with.
    /// </remarks>
    private static IReadOnlyList<CharacterRosterAppearanceState> Drawn(CharacterSummary summary) =>
        summary.Appearances ?? throw new InvalidOperationException(
            $"Character {summary.CharacterId} came back from a listing that drew nothing.");

    /// <summary>Puts one newly made piece on, the way the store holds a worn thing.</summary>
    /// <remarks>
    /// Two writes, because a thing and where it is are two rows: the piece goes into the world and
    /// is given an identity, and the worn position then says it holds that identity. It is the
    /// same pair creation uses and the same pair a move settles through.
    /// <para>
    /// The piece carries an attribute, an amplify type and an enchant count rather than zeroes, so
    /// that the fields of a drawn part other than the slot and the item are actually reached.
    /// </para>
    /// </remarks>
    private static Task<int> Wear(CharacterSide side, CharacterId character, int slot, int itemId) =>
        side.InUseCaseAsync(async token =>
        {
            var session = side.ItemStore.SessionIn(side.Scope.Current!);
            var core = StackableItemCore.Create(EquipmentKind, itemId, 1) with
            {
                Durability = 50, Attribute = 5, AmplifyType = 2, EnchantUpgradeCount = 9,
            };
            var record = await session.CreateAsync(ItemSnapshot.FromCore(core, false), token);
            var placed = await session.PutAsync(ContainerId.ForCharacter(character,
                InventoryContainerType.Equipment), slot, record, token);
            var worn = placed.ItemUid!.Value.Value;
            return worn;
        });

    /// <summary>Empties one worn position, leaving the thing that was in it.</summary>
    private static Task<int> TakeOff(CharacterSide side, CharacterId character, int slot) =>
        side.InUseCaseAsync(async token =>
        {
            var session = side.ItemStore.SessionIn(side.Scope.Current!);
            var worn = await session.GetAsync(ContainerId.ForCharacter(character,
                InventoryContainerType.Equipment), slot, token);
            if (worn is null) return 0;
            var bag = ContainerId.ForCharacter(character, InventoryContainerType.Main);
            var occupied = (await session.GetAllAsync(bag, token)).Select(item => item.SlotIndex).ToHashSet();
            var target = Enumerable.Range(9, 56).First(index => !occupied.Contains(index));
            await session.PutAsync(bag, target, await session.TakeAsync(worn, null, token), token);
            return 1;
        });

    private static async Task RunAndCleanUpAsync(string directory, Func<Task> body)
    {
        try
        {
            await body();
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
            new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
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
                new NewCharacterSpec(Account, name, 0, 0, CharacterBaseStats.Zero),
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
                new NewCharacterSpec(Account, undecodable, 0, 0, CharacterBaseStats.Zero),
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
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
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
                new NewCharacterSpec(Account, "Once"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.True(await repository.SoftDeleteAsync(id, CancellationToken.None));
            Assert.False(await repository.SoftDeleteAsync(id, CancellationToken.None));
        });

    [Fact]
    public Task TheRosterIsOrderedBySlot() => WithRepositoryAsync(async repository =>
    {
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Third"u8.ToArray(), 0, 2, CharacterBaseStats.Zero), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "First"u8.ToArray(), 0, 0, CharacterBaseStats.Zero), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Second"u8.ToArray(), 0, 1, CharacterBaseStats.Zero), CancellationToken.None);

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
                new NewCharacterSpec(new AccountId(2), "Theirs"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));

            // Names are unique server-wide, not per account.
            Assert.True(await repository.NameExistsAsync("Theirs"u8.ToArray(), CancellationToken.None));
        });
}
