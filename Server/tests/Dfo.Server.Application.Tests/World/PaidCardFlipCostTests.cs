using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Game.CardReward;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Tests.Inventory;
namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// A paid flip charged against the operator-supplied archive and a real character's gold.
/// </summary>
/// <remarks>
/// The whole path, with nothing between the file and the gold: the rule service reads
/// <c>etc/itemdropinfo_clearreward.etc</c>, the card service hands the price on, the command takes
/// it, and the account's own row in SQLite is what is lighter afterwards. The price is never
/// written down here -- it is read out of the archive in the test and compared against what the
/// account actually paid -- so a reading that drifted could not be met by a constant drifting with
/// it. What the archive declares at this level is asserted on its own in
/// <see cref="ClearRewardArchiveTests"/>.
/// <para>
/// Only the bag is a stand-in, for the reason the card slice's own tests give one: what is under
/// test is what the flip hands it, not how a row reaches storage.
/// </para>
/// </remarks>
public sealed class PaidCardFlipCostTests
{
    /// <summary>The level the endgame is fought at, and the level the character stands at.</summary>
    /// <remarks>
    /// <b>Not what a flip is priced at.</b> The archive prices a flip by the dungeon's own
    /// <c>[basis level]</c> -- see <see cref="BasisLevel"/> -- and this level is here so that a
    /// price which followed the character would come out a different number and be caught.
    /// </remarks>
    private const int Level = 86;

    /// <summary>
    /// What the archive sets this dungeon at, and so what a flip of it is priced at.
    /// </summary>
    /// <remarks>
    /// Dungeon 144's <c>[basis level]</c>, which the archive declares as 5; the layout these tests
    /// run over carries it. What the file charges there is not written down here -- it is read out
    /// of the archive by <see cref="Price(ServiceProvider)"/> -- so a reading that drifted could
    /// not be met by a constant drifting with it.
    /// </remarks>
    private const byte BasisLevel = 5;

    /// <summary>
    /// The level a clear settles at when its own experience is about to buy the next one.
    /// </summary>
    /// <remarks>
    /// One below <see cref="Level"/>, because that is the ordinary case: the level a clear settles
    /// at is read before its experience is paid and the level a flip happens at is read after, so
    /// a run that levelled the character is a run where the two differ by one. The archive prices
    /// both of those levels, and prices them differently from each other and from the dungeon's,
    /// which the test that uses this asserts rather than assumes.
    /// </remarks>
    private const int LevelBeforeTheAward = Level - 1;

    /// <summary>A piece to sit behind the card, so that a flip has something to hand over.</summary>
    private const int CardItemId = 10088621;

    /// <summary>
    /// A dungeon that declares nothing about the paid card, which 1042 of the archive's 1047 do.
    /// </summary>
    /// <remarks>
    /// 144 is the dungeon this subject's captures were taken in. Every test in this file other
    /// than the two about the switch is fought here, which is what makes them the unchanged case.
    /// </remarks>
    private const int OrdinaryDungeonId = 144;

    /// <summary>
    /// The dungeons that declare <c>[gold card use] 0</c> and that this server can enter.
    /// </summary>
    /// <remarks>
    /// All four of them rather than one, because "this dungeon refuses" and "the switch is read"
    /// are different claims and a single dungeon cannot separate them: a refusal keyed on the one
    /// identifier a test named would pass just as well. The fifth declaring dungeon, 3503, has no
    /// maze and so no run to flip a card in.
    /// </remarks>
    public static TheoryData<int> SwitchedOff => [100, 3700, 3901, 3902];

    private static readonly AccountId Account = new(9);

    private static readonly CharacterId Character = new(19);

    /// <summary>
    /// An account with more than the archive's price pays exactly that price and gets the card.
    /// </summary>
    /// <remarks>
    /// What the character carries afterwards is what was funded, less the archive's price, plus what the card
    /// itself paid -- all three, because the price alone would also be satisfied by a flip that
    /// charged correctly and handed nothing over.
    /// <para>
    /// The price is the dungeon's row and not the character's. Every test in this file is fought
    /// at <see cref="Level"/> and every one of them is charged <see cref="BasisLevel"/>'s price,
    /// which is what <see cref="A_clear_that_levelled_the_character_still_charges_what_it_announced"/>
    /// separates out.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_paid_flip_costs_the_archives_price_for_the_dungeons_level()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var price = Price(provider);
        var world = new Flip(provider, funded: price + 500);

        try
        {
            var result = await world.Turn(ClearRewardCardKind.Paid, 0);

            Assert.Equal(SelectCardStatus.Flipped, result.Status);
            Assert.Equal(price, result.GoldCharged);
            Assert.NotEqual(0, result.GoldPaid);
            Assert.Equal(500 + result.GoldPaid, await world.CarriedGold());
            Assert.Equal(CardItemId, world.Bag.Landed?.ItemId);
        }
        finally
        {
            world.Discard();
        }
    }

    /// <summary>
    /// An account one coin short of the archive's price is refused, and keeps the coin.
    /// </summary>
    /// <remarks>
    /// One coin short rather than empty: an account with nothing at all would be refused by a
    /// charge that compared the wrong way round just as surely as by one that compared correctly.
    /// The row is read back afterwards rather than the refusal being trusted, because "answered a
    /// refusal" and "left the gold where it was" are two different claims and the second is the
    /// one the player would notice.
    /// </remarks>
    [SkippableFact]
    public async Task An_account_a_coin_short_of_the_archives_price_is_refused_and_keeps_it()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var price = Price(provider);
        var world = new Flip(provider, funded: price - 1);

        try
        {
            var result = await world.Turn(ClearRewardCardKind.Paid, 0);

            Assert.Equal(SelectCardStatus.NotEnoughGold, result.Status);
            Assert.Equal(0, result.GoldCharged);
            Assert.Equal(0, result.GoldPaid);
            Assert.Empty(result.Slots);
            Assert.Equal(price - 1, await world.CarriedGold());
            Assert.Null(world.Bag.Landed);
        }
        finally
        {
            world.Discard();
        }
    }

    /// <summary>
    /// <b>A clear that levelled the character charges what it announced, not what the new level
    /// would be quoted.</b>
    /// </summary>
    /// <remarks>
    /// The defect this pins is not a corner. The result screen announces a price when the clear
    /// settles -- read before the clear's own experience is paid -- and the flip happens
    /// afterwards, at whatever level that experience bought. A price worked out again at flip time
    /// is a server that quotes one number and takes another.
    /// <para>
    /// <b>What made the two figures differ is gone, and the test is kept anyway.</b> Priced by the
    /// character, the archive's separate row per level made any run that levelled the character
    /// produce two different figures; priced by the dungeon, nothing the clear can do moves the
    /// number. That is a defect this server can no longer commit rather than one nobody is
    /// committing today, so the assertions are re-aimed rather than dropped: what is compared now
    /// is the announced figure against the <b>dungeon's</b> row, and against both of the
    /// character's rows that a re-quote would have reached. Delete this and a server that went
    /// back to pricing by the character would be caught only by the tests about the key, not by
    /// the one about the quote.
    /// </para>
    /// <para>
    /// The account is funded for all three prices, so a flip that charged the wrong one is caught
    /// by what it charged rather than by a refusal it could not pay for -- the failure has to name
    /// the defect, not a side effect of it. The three are asserted to differ first: equal ones
    /// would let a flip that re-quoted pass, and this test would then pin nothing at all.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_clear_that_levelled_the_character_still_charges_what_it_announced()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var announced = Price(provider, BasisLevel);
        var atTheSettledLevel = Price(provider, LevelBeforeTheAward);
        var afterTheAward = Price(provider, Level);

        // Three different figures: the dungeon's, and the two the character's level passes through
        // over this clear. Any of the three could have been charged and each names a different
        // defect.
        Assert.NotEqual(announced, atTheSettledLevel);
        Assert.NotEqual(announced, afterTheAward);
        Assert.NotEqual(atTheSettledLevel, afterTheAward);

        var funded = announced + atTheSettledLevel + afterTheAward;
        var world = new Flip(provider, funded: funded, settleAtLevel: LevelBeforeTheAward);

        try
        {
            // What the result screen was handed when the clear settled: the dungeon's price, not
            // the price of the level the character settled at.
            Assert.Equal(announced, world.Announced);

            // And what the settlement answers once the award has moved the character: the same
            // figure, because the clear fixed it rather than pricing anything afresh.
            Assert.Equal(announced, await world.PriceTheSettlementAnswersAt(Level));

            var result = await world.Turn(ClearRewardCardKind.Paid, 0, standingAt: Level);

            Assert.Equal(SelectCardStatus.Flipped, result.Status);
            Assert.Equal(announced, result.GoldCharged);
            Assert.Equal(funded - announced + result.GoldPaid, await world.CarriedGold());
        }
        finally
        {
            world.Discard();
        }
    }

    /// <summary>
    /// <b>A dungeon that switched its own paid card off offers no paid flip, and charges for
    /// none.</b>
    /// </summary>
    /// <remarks>
    /// The archive prices this level -- asserted first, so that the refusal below is the dungeon's
    /// declaration and not a level nobody priced -- and the account is funded well past that
    /// price, so a flip that went through would go through. What it gets instead is a refusal that
    /// left the character's gold exactly as it found it.
    /// <para>
    /// <b>The free flip of the same clear is turned afterwards and is untouched.</b> That is the
    /// half that says what was switched off: a change that had closed the whole card panel, or
    /// broken the settlement, would refuse this one too and would otherwise look identical.
    /// </para>
    /// </remarks>
    [SkippableTheory]
    [MemberData(nameof(SwitchedOff))]
    public async Task A_dungeon_that_switched_its_paid_card_off_refuses_the_flip(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var price = Price(provider);
        var world = new Flip(
            provider, funded: price + 500, settleAtLevel: Level, dungeonId: dungeonId);

        try
        {
            // The clear named no price when it settled, which is what the result screen was given.
            Assert.Null(world.Announced);

            var paid = await world.Turn(ClearRewardCardKind.Paid, 0);

            Assert.Equal(SelectCardStatus.NotPriced, paid.Status);
            Assert.Equal(0, paid.GoldCharged);
            Assert.Equal(0, paid.GoldPaid);
            Assert.Empty(paid.Slots);
            Assert.Equal(price + 500, await world.CarriedGold());

            // Only the paid card was switched off.
            var free = await world.Turn(ClearRewardCardKind.Free, 0);

            Assert.Equal(SelectCardStatus.Flipped, free.Status);
            Assert.Equal(0, free.GoldCharged);
            Assert.NotEmpty(free.Slots);
        }
        finally
        {
            world.Discard();
        }
    }

    /// <summary>
    /// <b>The same clear in a dungeon that declares no switch is charged the archive's price.</b>
    /// </summary>
    /// <remarks>
    /// The control for the four above, settled the same way and funded the same amount, differing
    /// in the dungeon and in nothing else. Without it, a change that refused every paid flip
    /// outright would satisfy every assertion those four make.
    /// </remarks>
    [SkippableFact]
    public async Task A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var price = Price(provider);
        var world = new Flip(
            provider, funded: price + 500, settleAtLevel: Level, dungeonId: OrdinaryDungeonId);

        try
        {
            Assert.Equal(price, world.Announced);

            var paid = await world.Turn(ClearRewardCardKind.Paid, 0);

            Assert.Equal(SelectCardStatus.Flipped, paid.Status);
            Assert.Equal(price, paid.GoldCharged);
            Assert.Equal(500 + paid.GoldPaid, await world.CarriedGold());
        }
        finally
        {
            world.Discard();
        }
    }

    /// <summary>What the archive charges for a paid flip of this dungeon.</summary>
    /// <remarks>
    /// Asserted to exist and to be a real price before anything is built on it. A level the
    /// archive stopped pricing would otherwise turn both tests above into assertions about a
    /// refusal nobody meant to make.
    /// </remarks>
    private static int Price(ServiceProvider provider) => Price(provider, BasisLevel);

    /// <summary>What the archive charges for a paid flip of a dungeon set at that level.</summary>
    private static int Price(ServiceProvider provider, int level)
    {
        var price = Rules(provider).PaidFlipCost(level);

        Assert.NotNull(price);
        Assert.True(price > 0);
        return price.Value;
    }

    private static ClearRewardRuleService Rules(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IClearRewardRateSource>(),
            provider.GetRequiredService<IGoldDropTableSource>(),
            provider.GetRequiredService<IDropItemPoolSource>(),
            provider.GetRequiredService<IDungeonDefinitionSource>(),
            new ClearRewardRateOptions());

    /// <summary>One settled clear, one funded account, and the command that turns a card over.</summary>
    private sealed class Flip
    {
        private static readonly MazeCell Cell = new(0, 0);

        private static DungeonLayout LayoutFor(int dungeonId) => new(
            DungeonId: dungeonId,
            MazeIndex: 0,
            Width: 1,
            Height: 1,
            StartCell: Cell,
            BossCell: Cell,
            StartRoomMapId: 55611,
            Seed: 0,
            Rooms: new Dictionary<MazeCell, int> { [Cell] = 55611 },
            BasisLevel: BasisLevel,
            ExperienceWeight: 1.45);

        private readonly SqliteCharacterCarriedGoldTable _carried;
        private readonly CardRewardDomainService _cards;
        private readonly SelectCardCommandHandler _handler;
        private readonly string _path;

        /// <param name="settleAtLevel">
        /// The character level to settle this clear at through the card service, which fixes both
        /// its faces and its price -- the price at the dungeon's level rather than at this one.
        /// Null settles nothing and puts two known faces down instead, priced the same way: the
        /// tests that read what a flip handed over need a piece behind the position they turn, and
        /// a drawn face may hold none.
        /// </param>
        /// <param name="dungeonId">
        /// Which dungeon this clear was fought in. It decides one thing and only one: whether the
        /// dungeon has switched its own paid card off. Defaulted to the ordinary dungeon this
        /// subject's captures were taken in, so every test written before the switch existed runs
        /// against exactly what it ran against then.
        /// </param>
        public Flip(
            ServiceProvider provider,
            int funded,
            int? settleAtLevel = null,
            int dungeonId = OrdinaryDungeonId)
        {
            this._path = Path.Combine(Path.GetTempPath(), $"dfo-flip-{Guid.NewGuid():N}.db");
            var database = new SqliteDatabase(this._path, new DomainTransactionScope());
            OpenCharacter(database);
            this._carried = new SqliteCharacterCarriedGoldTable(database);
            this._carried.AddAsync(Character, funded, int.MaxValue, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            var runs = new DungeonRunDomainService();
            runs.Begin(Character, LayoutFor(dungeonId), difficulty: 1);
            this._cards = new CardRewardDomainService(Rules(provider), runs);

            if (settleAtLevel is { } settledAt)
            {
                // Settled the way a clear settles it, so the announced price is the seam's answer
                // rather than a figure this fixture wrote down and then agreed with.
                this.Announced = this._cards
                    .SettleAsync(
                        new ClearRewardSettlement(Character, settledAt), CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult()?.PaidFlipCost;
            }
            else
            {
                // The faces are put down rather than drawn, because what is under test is the
                // price of turning one over. Both hold a piece at their first position, so a flip
                // of either has something to hand over and the answer cannot come from the wrong
                // face. The price goes down with them, which is where a clear fixes it.
                this.Announced = Price(provider);
                runs.FixClearRewardCards(
                    Character,
                    Face(ClearRewardCardKind.Free),
                    Face(ClearRewardCardKind.Paid),
                    this.Announced);
            }

            this._handler = new SelectCardCommandHandler(
                this._cards,
                runs,
                new OneBag(this.Bag),
                new OneCharacter(new RealCarriedGold(this._carried)),
                TimeProvider.System);
        }

        /// <summary>
        /// A character whose gold is the real table over the real file.
        /// </summary>
        /// <remarks>
        /// Only the gold is real here. The cap is the archive's floor for level eighty-six, four
        /// hundred million, which no case in this file goes near -- so the cap is passed straight
        /// through rather than read, and a case that started to depend on it would have to say so.
        /// </remarks>
        private sealed class RealCarriedGold(SqliteCharacterCarriedGoldTable carried) : ICharacter
        {
            public CharacterId Id => Character;

    public ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken cancellationToken) => throw new NotSupportedException();


            public ValueTask<ItemUsability> CanUseAsync(
                int itemId, CancellationToken cancellationToken) =>
                throw new NotSupportedException("This subject is about a flip's price.");

            public async ValueTask<CharacterProgressionState?> ProgressAsync(CancellationToken cancellationToken) =>
                await carried.FindAsync(Character, cancellationToken) is { } row
                    ? new CharacterProgressionState(checked((byte)row.Level), 0) : null;

            public ValueTask<CharacterIdentityState?> IdentityAsync(
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("This subject is about a flip's price.");

            public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("This subject is about a flip's price.");

            public async ValueTask<int> GoldAsync(CancellationToken cancellationToken) =>
                (await carried.FindAsync(Character, cancellationToken))?.Carried ?? 0;

            public ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken) =>
                carried.AddAsync(Character, amount, int.MaxValue, cancellationToken);

            public ValueTask<int?> TrySpendGoldAsync(
                int amount, CancellationToken cancellationToken) =>
                carried.TryTakeAsync(Character, amount, cancellationToken);
        }

        public RecordingBag Bag { get; } = new();

        /// <summary>What this clear fixed a paid flip's price at when it settled.</summary>
        public int? Announced { get; }

        public async ValueTask<SelectCardResult> Turn(ClearRewardCardKind kind, int index, int standingAt = Level)
        {
            await using var connection = new SqliteConnection($"Data Source={this._path}");
            await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE characters SET level=@level WHERE character_id=@id";
            update.Parameters.AddWithValue("@level", standingAt);
            update.Parameters.AddWithValue("@id", Character.Value);
            await update.ExecuteNonQueryAsync();
            return await this._handler.HandleAsync(
                new CommandExecutionContext(Guid.NewGuid(), typeof(SelectCardCommand),
                    typeof(SelectCardCommandHandler), CommandExecutionKind.Workflow),
                new SelectCardCommand(Account, Character, kind, index), CancellationToken.None);
        }

        /// <summary>What the settlement says a paid flip costs, asked at that level.</summary>
        /// <remarks>
        /// The question the result screen asks, asked again after the award. A settlement that
        /// worked the price out afresh would answer the new level's figure here.
        /// </remarks>
        public async ValueTask<int?> PriceTheSettlementAnswersAt(int standingAt) =>
            (await this._cards.SettleAsync(
                new ClearRewardSettlement(Character, standingAt), CancellationToken.None))
                ?.PaidFlipCost;

        /// <summary>What the character's own row says it carries.</summary>
        public async ValueTask<int> CarriedGold() =>
            (await this._carried.FindAsync(Character, CancellationToken.None))?.Carried ?? 0;

        /// <remarks>
        /// The gold lives on the character, so the character has to exist. Level eighty-six is the
        /// band these captures were taken in, and its archive floor is four hundred million --
        /// comfortably above anything this subject funds, so the cap never enters these cases.
        /// </remarks>
        private static void OpenCharacter(SqliteDatabase database)
        {
            using var connection = new SqliteConnection(database.ConnectionString);
            connection.Open();
            using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO characters(character_id, account_id, name, job, level)
                VALUES (@characterId, @accountId, 'flipper', 1, 86);
                INSERT INTO character_stats(character_id) VALUES (@characterId);
                """;
            insert.Parameters.AddWithValue("@characterId", Character.Value);
            insert.Parameters.AddWithValue("@accountId", Account.Value);
            insert.ExecuteNonQuery();
        }

        public void Discard()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this._path);
            }
            catch (IOException)
            {
                // A temporary file the operating system will clear. Failing here would report a
                // passing assertion as a broken one.
            }
        }

        private static ClearRewardCardFace Face(ClearRewardCardKind kind) =>
            new(
                kind,
                [
                    new ClearRewardCardSlot(
                        0, new ClearRewardCardItem(CardItemId, 1, DropRarity.Common, 25)),
                    new ClearRewardCardSlot(1, null),
                ],
                FaceGold);

        /// <summary>What the hand-written face says a flip of it pays.</summary>
        private const int FaceGold = 37;
    }

    /// <summary>A bag that takes whatever it is offered and remembers the last of it.</summary>
    private sealed class RecordingBag : IItemContainer
    {
        private const int Slot = 3;

        public InventoryItemCoreState? Landed { get; private set; }

        public ContainerId Id => new("bag");

        public InventoryContainerType Type => InventoryContainerType.Main;

        public ContainerShape Shape => new(InventoryContainerType.Main, 56);

        public bool IsRemovable => false;

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken)
        {
            this.Landed = StackableItemCore.Create(2, itemId, count);
            return ValueTask.FromResult<ItemPlacementOutcome>(
                new ItemPlacementOutcome.PutInEmptySlot(Slot, count));
        }

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Landed is { } core
                    ? new ContainerSlotItem(slotIndex, core, ItemWorldClass.Interchangeable)
                    : null);

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.Landed = core;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fixture is only added to and read back.");
    }

    /// <summary>A character bound to that one bag.</summary>
    private sealed class OneBag(IItemContainer bag) : ICharacterContainerDomainService
    {
        public IItemContainer? FindContainer(
            CharacterId characterId, InventoryContainerType type) => bag;

        public IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type) =>
            bag;

        public IReadOnlyList<IItemContainer> GetContainers(
            CharacterId characterId, InventoryContainerType type) => [bag];

        public void Register(CharacterId characterId, IItemContainer container) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool Remove(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool UseContainer(CharacterId characterId, ContainerId containerId) =>
            throw new NotSupportedException("This fixture only answers.");

        public void Clear(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only answers.");
    }
}
