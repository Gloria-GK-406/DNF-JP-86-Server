using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ExperienceItemUseTests
{
    private const int FixedBook = 690001552;
    private const int PercentBook = 690001555;
    private static readonly BusinessSlot Source = new(BusinessInventorySpace.Consumable, 4);

    [SkippableTheory]
    [InlineData(690001552, 1_000)]
    [InlineData(690001553, 10_000)]
    [InlineData(690001554, 100_000)]
    [InlineData(690001555, -1)]
    public async Task Real_tau_books_add_their_declared_experience_instead_of_only_consuming(
        int itemId,
        int declaredGain)
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")),
            "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new CeraShopFixture(useArchive: true);
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(69, itemId, 1);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var gain = declaredGain >= 0 ? declaredGain : checked((int)(
            (fixture.Host.ExperienceForLevel(21) - fixture.Host.ExperienceForLevel(20)) / 100));

        var result = await fixture.Host.Services.GetRequiredService<ICommandBus>()
            .SendAsync(WholeUse.Command(fixture.Host.Services, fixture.Host.Session, Source, itemId));

        Assert.Equal(ItemUseStatus.Used, result.Status);
        Assert.Equal(before + gain, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(0, fixture.Host.Count(itemId));
    }

    [Fact]
    public async Task Synthetic_percentage_uses_one_percent_of_the_current_level_cost_floor()
    {
        using var fixture = Fixture(PercentBook,
            new(ExperienceItemGrantKind.PercentOfCurrentLevel, 1, 20, 84, true));
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(69, PercentBook, 2);
        var before = fixture.Host.ExperienceForLevel(20);
        var expected = (fixture.Host.ExperienceForLevel(21) - before) / 100;

        var result = await Use(fixture, PercentBook);

        Assert.Equal(ItemUseStatus.Used, result.Status);
        Assert.Equal(1, fixture.Host.Count(PercentBook));
        Assert.Equal(checked((int)(before + expected)),
            fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Theory]
    [InlineData(19, ItemUseStatus.LevelTooLow, 2)]
    [InlineData(20, ItemUseStatus.Used, 1)]
    [InlineData(84, ItemUseStatus.Used, 1)]
    [InlineData(85, ItemUseStatus.NotAUsableItem, 2)]
    public async Task Synthetic_level_boundaries_preserve_refused_books(
        byte level,
        ItemUseStatus expected,
        int remaining)
    {
        using var fixture = Fixture(FixedBook,
            new(ExperienceItemGrantKind.Fixed, 1_000, 20, 84, true));
        fixture.Host.SetLevel(level);
        await fixture.Host.Seed(69, FixedBook, 2);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");

        var result = await Use(fixture, FixedBook);

        Assert.Equal(expected, result.Status);
        Assert.Equal(remaining, fixture.Host.Count(FixedBook));
        var maximumExperience = fixture.Host.Services.GetRequiredService<IExperienceRuleService>()
            .CharacterScale().MaximumExperience;
        var expectedExperience = expected == ItemUseStatus.Used
            ? Math.Min(before + 1_000, checked((int)maximumExperience))
            : before;
        Assert.Equal(expectedExperience,
            fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Expired_source_is_refused_without_experience_or_consumption()
    {
        using var fixture = Fixture(FixedBook,
            new(ExperienceItemGrantKind.Fixed, 1_000, 20, 84, true));
        fixture.Host.SetLevel(20);
        await fixture.Host.SeedCore(69, StackableItemCore.Create(2, FixedBook, 2) with
        {
            ExpireTime = checked((int)fixture.Host.Clock.GetUtcNow().AddSeconds(-1).ToUnixTimeSeconds()),
        });
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");

        var result = await Use(fixture, FixedBook);

        Assert.Equal(ItemUseStatus.NotAUsableItem, result.Status);
        Assert.Equal(2, fixture.Host.Count(FixedBook));
        Assert.Equal(before, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Experience_write_failure_rolls_back_the_book_and_progress()
    {
        using var fixture = Fixture(FixedBook,
            new(ExperienceItemGrantKind.Fixed, 1_000, 20, 84, true));
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(69, FixedBook, 2);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        fixture.Host.Sql("CREATE TRIGGER fail_experience_book BEFORE UPDATE OF exp ON characters "
            + "BEGIN SELECT RAISE(ABORT,'experience book'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Use(fixture, FixedBook));

        Assert.Equal(2, fixture.Host.Count(FixedBook));
        Assert.Equal(before, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task First_response_and_replay_project_progress_without_paying_twice()
    {
        using var fixture = Fixture(FixedBook,
            new(ExperienceItemGrantKind.Fixed, 1_000, 20, 84, true));
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(69, FixedBook, 2);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var counter = new GameInboundFrameCounter();

        UseStackableRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1);
            var body = new byte[11];
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 69);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), FixedBook);
            var metadata = new GameMessageMetadata(1, 0x002C, 0, 1)
            {
                InboundOrdinal = ordinal,
                SequenceIsFresh = fresh,
            }.WithRawBody(body);
            return new(metadata, new(InventoryContainerType.Main, 69), 0, FixedBook);
        }

        var dispatcher = fixture.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(fixture.Host.Connection), Request(), default);

        Assert.Contains(fixture.Host.Sender.Messages, message => message is UseStackableResponse);
        Assert.Contains(fixture.Host.Sender.Messages.Select(fixture.Encode),
            frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x0025);
        Assert.Equal(before + 1_000,
            fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(1, fixture.Host.Count(FixedBook));

        await dispatcher.DispatchAsync(new(fixture.Host.Connection), Request(), default);

        var replay = Assert.Single(fixture.Host.Sender.Batches);
        Assert.Contains(replay.Frames,
            frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x0025);
        Assert.DoesNotContain(replay.Messages, message => message is UseStackableResponse);
        Assert.Equal(before + 1_000,
            fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(1, fixture.Host.Count(FixedBook));
    }

    [Fact]
    public async Task Recognized_but_unreadable_experience_item_never_falls_through_to_consume_only()
    {
        using var fixture = Fixture(FixedBook,
            new(ExperienceItemGrantKind.Fixed, 0, 20, 84, false));
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(69, FixedBook, 2);

        var result = await Use(fixture, FixedBook);

        Assert.Equal(ItemUseStatus.NotAUsableItem, result.Status);
        Assert.Equal(2, fixture.Host.Count(FixedBook));
    }

    private static Task<ItemUseResult> Use(CeraShopFixture fixture, int itemId) =>
        fixture.Host.Services.GetRequiredService<ICommandBus>()
            .SendAsync(WholeUse.Command(fixture.Host.Services, fixture.Host.Session, Source, itemId)).AsTask();

    private static CeraShopFixture Fixture(int itemId, ExperienceItemDefinition definition)
    {
        var fixture = new CeraShopFixture(configure: services =>
        {
            services.AddSingleton<IExperienceItemDefinitionSource>(
                new ExperienceDefinitions((itemId, definition)));
            services.AddSingleton<IItemUseRuleDefinitionSource>(
                new FakeItemUseRules((itemId, ItemUseRules.Potion(cooldownMilliseconds: 0))));
            services.AddSingleton<ICharacterSkillPointDomainService>(new TestSkillPoints());
        });
        fixture.Content.Placements[itemId] = new(2, 10,
            new(BusinessInventorySpace.Consumable, 0, 55));
        return fixture;
    }

    private sealed class ExperienceDefinitions(
        params (int ItemId, ExperienceItemDefinition Definition)[] definitions)
        : IExperienceItemDefinitionSource
    {
        private readonly IReadOnlyDictionary<int, ExperienceItemDefinition> entries =
            definitions.ToDictionary(entry => entry.ItemId, entry => entry.Definition);

        public ValueTask<ExperienceItemDefinition?> GetAsync(
            int itemId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(entries.GetValueOrDefault(itemId));
    }

    private sealed class TestSkillPoints : ICharacterSkillPointDomainService
    {
        private static readonly CharacterSkillPointLedger Ledger = new(321, 0, 0, 0, []);

        public ValueTask<CharacterSkillPointLedger?> LedgerAsync(
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult<CharacterSkillPointLedger?>(Ledger);

        public ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(Ledger.ToProtocolMirror());
    }
}
