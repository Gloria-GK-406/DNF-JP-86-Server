using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The entry rules running against the operator-supplied archive through the production
/// registration, so nothing on the path from archive bytes to entry answer is stubbed.
/// </summary>
/// <remarks>
/// The rule tests prove each rule in isolation against controlled definitions. These prove
/// the rules still mean something once real content flows through them, which is the check
/// an earlier attempt at this outcome lacked.
/// </remarks>
public sealed class WorldContentArchiveTests
{
    [Theory]
    [InlineData(49999, false)]
    [InlineData(50000, true)]
    [InlineData(51000, true)]
    [InlineData(51001, false)]
    [InlineData(55999, false)]
    [InlineData(56000, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    [InlineData(61670, false)]
    [InlineData(110899, false)]
    [InlineData(110900, true)]
    [InlineData(110999, true)]
    [InlineData(111000, false)]
    public void Named_ranges_match_A21_independently_of_champion_rank(int code, bool named)
    {
        var rules = new DungeonRoomRuleService(null!, null!, null!, null!);
        var actors = rules.Describe([new(code, 1, 0), new(code, 1, 1)]);
        Assert.All(actors, actor => Assert.Equal(named, actor.IsNamedMonster));
    }

    [SkippableFact]
    public async Task Quest_rooms_resolve_monster_level_from_both_map_fields_and_dungeon_basis()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var rooms = new DungeonRoomRuleService(
            scope.GetRequiredService<IDungeonRoomActorSource>(),
            scope.GetRequiredService<IDungeonRoomDropSource>(),
            new ObjectDropRuleService(scope.GetRequiredService<IDropRateTableSource>(),
                scope.GetRequiredService<IGoldDropTableSource>(), scope.GetRequiredService<IDropItemPoolSource>(), new()), scope.GetRequiredService<IDungeonRoomObjectSource>());
        var dungeon = await scope.GetRequiredService<IDungeonDefinitionSource>()
            .GetAsync(146, CancellationToken.None);
        Assert.NotNull(dungeon);
        Assert.Equal(11, dungeon.BasisLevel);
        var ordinary = new MazeCell(0, 0);
        var boss = new MazeCell(1, 0);
        var layout = new DungeonLayout(
            DungeonId: 146,
            MazeIndex: 1,
            Width: 2,
            Height: 1,
            StartCell: ordinary,
            BossCell: boss,
            StartRoomMapId: 55623,
            Seed: 1,
            Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [ordinary] = DungeonCellDefinition.Fixed(55623), [boss] = DungeonCellDefinition.Fixed(55625)},
            BasisLevel: dungeon.BasisLevel) { NamedMonsterCodes = [65311] };

        async Task<DungeonRoomContents> Describe(MazeCell cell, int mapId) =>
            await rooms.DescribeAsync(
                new DungeonCellCreationContext(
                    DungeonRunId.New(), layout, cell, mapId, 0,
                    DateTimeOffset.UnixEpoch, []),
                CancellationToken.None);

        var absolute = await Describe(ordinary, 55623);
        var forestMonsters = absolute.Actors.Where(actor => actor.Actor.Code == 65311).ToArray();
        Assert.NotEmpty(forestMonsters);
        Assert.All(forestMonsters, actor => Assert.Equal(10, actor.Actor.Level));
        Assert.All(forestMonsters, actor => Assert.True(actor.IsNamedMonster));

        var relative = await Describe(boss, 55625);
        Assert.Equal(11, Assert.Single(relative.Actors, actor => actor.Actor.Code == 59753)
            .Actor.Level);
        Assert.True(Assert.Single(relative.Actors, actor => actor.Actor.Code == 59753).IsNamedMonster);
    }

    [SkippableTheory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(23)]
    [InlineData(60)]
    public async Task Historical_mirror_rooms_override_old_absolute_levels_without_mutating_PVF(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var actors = scope.GetRequiredService<IDungeonRoomActorSource>();
        var rooms = new DungeonRoomRuleService(actors,
            scope.GetRequiredService<IDungeonRoomDropSource>(),
            new ObjectDropRuleService(scope.GetRequiredService<IDropRateTableSource>(),
                scope.GetRequiredService<IGoldDropTableSource>(), scope.GetRequiredService<IDropItemPoolSource>(), new()),
            scope.GetRequiredService<IDungeonRoomObjectSource>());
        var structures = new DungeonStructureRuleService(scope.GetRequiredService<IDungeonDefinitionSource>(),
            scope.GetRequiredService<IDungeonMapDefinitionSource>());
        var original = Assert.IsType<DungeonLayout>(await structures.ResolveAsync(
            dungeonId, DungeonEntryFacts.None, 42, default));
        var layout = original with { BasisLevel = 86, FixedMonsterLevel = 86, Mode = new DungeonRunMode.Mirror(new(8000, 31, 14299)) };
        var scaled = 0;
        foreach (var cell in layout.Cells)
        foreach (var map in cell.Value.MapCandidates.Concat(cell.Value.BossMapCandidates).Distinct())
        {
            var before = (await actors.GetAsync(map, default)).ToArray();
            var contents = await rooms.DescribeAsync(new(DungeonRunId.New(), layout, cell.Key, map,
                0, DateTimeOffset.UnixEpoch, []), default);
            for (var index = 0; index < before.Length; index++)
            {
                var actor = before[index];
                if (actor.ActorType > 3 || actor.Declaration is { IsDummy: true }
                    || actor.Declaration is { Source: not (DungeonActorSpawnSource.Monster or DungeonActorSpawnSource.ConditionalMonster) }) continue;
                Assert.Equal((byte)86, contents.Actors[index].Actor.Level);
                Assert.Null(contents.Actors[index].Actor.BasisLevelOffset);
                scaled++;
            }
            Assert.Equal(before, await actors.GetAsync(map, default));
        }
        Assert.True(scaled > 0);
        Assert.Equal((byte)55, original.BasisLevel);
        Assert.Null(original.FixedMonsterLevel);
    }

    [SkippableFact]
    public async Task A_high_level_character_is_offered_a_different_quest_set_than_a_low_level_one()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var service = CreateService(scope);

        var novice = await service.LoadAsync(Subject(level: 1), CancellationToken.None);
        var veteran = await service.LoadAsync(Subject(level: 40), CancellationToken.None);

        Assert.NotEmpty(novice.AvailableQuests.Quests);
        Assert.NotEmpty(veteran.AvailableQuests.Quests);
        Assert.NotEqual(
            novice.AvailableQuests.Quests.Count,
            veteran.AvailableQuests.Quests.Count);
    }

    [SkippableFact]
    public async Task Clearing_a_quest_removes_it_from_the_offered_set()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var service = CreateService(scope);

        var before = await service.LoadAsync(Subject(level: 40), CancellationToken.None);
        var cleared = before.AvailableQuests.Quests.Take(5).Select(quest => quest.QuestId).ToArray();

        var after = await service.LoadAsync(
            Subject(level: 40, clearedQuestIds: cleared),
            CancellationToken.None);

        Assert.DoesNotContain(after.AvailableQuests.Quests, quest => cleared.Contains(quest.QuestId));

        // The set does not simply shrink by the number cleared, and it is not the cleared ones
        // that leave it either. On the A21 archive these five take forty-two offers to
        // thirty-eight rather than thirty-seven, and quest 63 -- which was not among them --
        // stops being offered as well. Both directions are the quest graph's: clearing satisfies
        // prerequisites and trips collisions, and which of the two moved 63 is not established
        // here. The counts are pinned so a change stays visible; what this test is about is the
        // removal asserted above.
        Assert.Equal(42, before.AvailableQuests.Quests.Count);
        Assert.Equal(38, after.AvailableQuests.Quests.Count);
    }

    [SkippableFact]
    public async Task Real_job_restrictions_change_the_offered_set_for_a_supported_subject()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var service = CreateService(scope);

        (int Level, byte GrowType)? differingSubject = null;
        for (var level = 1; level <= 99; level++)
        {
            for (byte growType = 0; growType <= 15; growType++)
            {
                var swordman = await service.LoadAsync(
                    Subject(level: level, job: 0, growType: growType),
                    CancellationToken.None);
                var mage = await service.LoadAsync(
                    Subject(level: level, job: 3, growType: growType),
                    CancellationToken.None);
                if (!swordman.AvailableQuests.Quests.Select(quest => quest.QuestId)
                        .SequenceEqual(mage.AvailableQuests.Quests.Select(quest => quest.QuestId)))
                {
                    differingSubject = (level, growType);
                    break;
                }
            }

            if (differingSubject is not null)
                break;
        }

        Assert.NotNull(differingSubject);
    }

    [SkippableFact]
    public async Task The_daily_challenge_opens_slots_for_a_level_the_content_serves()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var service = CreateService(scope);

        var result = await service.LoadAsync(Subject(level: 60), CancellationToken.None);

        Assert.NotEmpty(result.DailyChallenge.Groups);
        Assert.All(result.DailyChallenge.Groups, group => Assert.True(group.OpenSlotCount >= 0));
        Assert.Contains(result.DailyChallenge.Groups, group => group.OpenSlotCount > 0);
    }

    [SkippableFact]
    public async Task A_collection_box_reflects_the_items_the_character_has_collected()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var boxes = scope.GetRequiredService<ICollectionBoxDefinitionSource>();
        var definition = (await boxes.GetAllAsync(CancellationToken.None))[0];
        var collected = definition.Slots.Take(3).Select(slot => slot.ItemTemplateId).ToArray();

        var service = CreateService(scope);
        var result = await service.LoadAsync(
            Subject(collectionBoxes:
                [new CollectionBoxProgressSubject(definition.BoxIndex, collected)]),
            CancellationToken.None);

        var box = Assert.Single(
            result.CollectionBoxes,
            candidate => candidate.BoxIndex == definition.BoxIndex);
        Assert.Equal(definition.Slots.Count, box.Cells.Count);
        Assert.Equal(collected.Length, box.FilledCount);
    }

    /// <remarks>
    /// This is the test that would have caught the id-space defect. Equipment identifiers and
    /// creature identifiers are disjoint in this archive — 408 creatures numbered up to about
    /// twenty thousand, 745 creature-equipment entries starting at 63000, and zero values in
    /// common — so an implementation that hands the equipped item to the creature lookup
    /// returns null for every character alive. Driving a real equipped item through the real
    /// content is the only way to see that; injecting a creature identifier taken from the
    /// creature source, as the earlier version of this test did, bypasses the bug entirely.
    /// </remarks>
    [SkippableFact]
    public async Task A_real_equipped_creature_item_resolves_to_its_welcome_lines()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var creatures = scope.GetRequiredService<ICreatureDefinitionSource>();

        // Faras: the first creature-equipment entry in the archive, denoting creature 1.
        const int FarasItem = 63000;
        var byEquipment = await creatures.FindByEquipmentAsync(FarasItem, CancellationToken.None);
        Assert.NotNull(byEquipment);
        Assert.NotEqual(FarasItem, byEquipment!.CreatureId);

        var service = CreateService(scope);
        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: FarasItem),
            CancellationToken.None);

        Assert.NotNull(result.PetWelcome);
        Assert.Equal(byEquipment.CreatureId, result.PetWelcome!.CreatureId);
        Assert.NotEmpty(result.PetWelcome.Lines);
    }

    [SkippableFact]
    public async Task A_creature_identifier_is_not_accepted_where_an_equipment_identifier_belongs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var creatures = scope.GetRequiredService<ICreatureDefinitionSource>();
        var scripted = (await creatures.GetAllAsync(CancellationToken.None))
            .First(creature => creature.HasAmbientLines);

        // A creature identifier is not an equipment identifier, and the archive shares none
        // between the two spaces, so this must resolve to nothing rather than to itself.
        Assert.Null(await creatures.FindByEquipmentAsync(scripted.CreatureId, CancellationToken.None));

        var result = await CreateService(scope).LoadAsync(
            Subject(equippedCreatureItemId: scripted.CreatureId),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [SkippableFact]
    public async Task An_item_that_is_not_creature_equipment_yields_no_welcome()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();

        var result = await CreateService(scope).LoadAsync(
            Subject(equippedCreatureItemId: 1),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    /// <remarks>
    /// The same arrival rule the unit tests pin against a hand-built map, run against the
    /// real one. If the archive's geometry were read wrongly this would place the character
    /// somewhere else while every stubbed test still passed.
    /// </remarks>
    [SkippableFact]
    public async Task The_restricted_channel_arrival_resolves_against_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var placement = new EntryPlacementRuleService(
            scope.GetRequiredService<ITownDefinitionSource>(),
            ChannelEntryOptions.Legacy);

        var result = await placement.ResolveAsync(
            new ChannelEntryIdentity(100, 10161, ChannelEntryKind.SaintHorn),
            persistedTownId: 1,
            CancellationToken.None);

        Assert.True(result.Spawn.IsTransient);
        Assert.False(result.PersistsPosition);
        Assert.Equal(17, result.Spawn.TownId);
        Assert.Equal(0, result.Spawn.AreaId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
        Assert.False(result.Spawn.SharesTownPresence);
    }

    [SkippableFact]
    public async Task An_ordinary_arrival_resolves_against_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var placement = new EntryPlacementRuleService(
            scope.GetRequiredService<ITownDefinitionSource>(),
            ChannelEntryOptions.Legacy);

        var result = await placement.ResolveAsync(
            new ChannelEntryIdentity(11, 10011, ChannelEntryKind.General),
            persistedTownId: 0,
            CancellationToken.None);

        Assert.False(result.Spawn.IsTransient);
        Assert.True(result.PersistsPosition);
        Assert.Equal(1, result.Spawn.TownId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
    }

    [SkippableFact]
    public async Task Time_gate_login_falls_back_to_the_initial_town_from_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var towns = scope.GetRequiredService<ITownDefinitionSource>();
        var timeGate = await towns.GetAsync(12, CancellationToken.None);
        Assert.NotNull(timeGate);
        Assert.Null(timeGate.Gate);
        var placement = new EntryPlacementRuleService(towns, ChannelEntryOptions.Legacy);

        var result = await placement.ResolveAsync(
            new ChannelEntryIdentity(11, 10011, ChannelEntryKind.General), 12, CancellationToken.None);

        Assert.Equal(1, result.Spawn.TownId);
        Assert.Equal(474, result.Spawn.X);
        Assert.Equal(234, result.Spawn.Y);
    }

    private static WorldContentEntryRuleService CreateService(IServiceProvider provider) =>
        new(new QuestContentRuleService(
                provider.GetRequiredService<IQuestDefinitionSource>(),
                provider.GetRequiredService<IQuestParameterSource>()),
            provider.GetRequiredService<IDailyActivityDefinitionSource>(),
            provider.GetRequiredService<ICollectionBoxDefinitionSource>(),
            provider.GetRequiredService<ICreatureDefinitionSource>());

    private static WorldContentSubject Subject(
        int level = 1,
        byte job = 0,
        byte growType = 0,
        IReadOnlyCollection<int>? clearedQuestIds = null,
        IReadOnlyList<CollectionBoxProgressSubject>? collectionBoxes = null,
        int equippedCreatureItemId = 0) =>
        new(level,
            job,
            growType,
            (clearedQuestIds ?? []).ToDictionary(questId => questId, _ => 1),
            [],
            [],
            collectionBoxes ?? [],
            equippedCreatureItemId,
            EquippedCreatureLevel: 0);
}

/// <summary>
/// Locates the operator-supplied archive and builds the production content registration
/// over it. The archive is excluded from version control, so these tests skip rather than
/// fail where it is absent.
/// </summary>
internal static class ContentArchive
{
    public const string SkipReason =
        "Script.pvf not found. Place the archive in the repository root or Data/Pvf/.";

    public static string? Path { get; } = Locate();

    public static bool Available => Path is not null;

    public static ServiceProvider CreateContentProvider()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pvf:ArchivePath"] = Path })
            .Build();

        new InfrastructureModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(directory.FullName, "Script.pvf"),
                         System.IO.Path.Combine(directory.FullName, "Data", "Pvf", "Script.pvf"),
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
