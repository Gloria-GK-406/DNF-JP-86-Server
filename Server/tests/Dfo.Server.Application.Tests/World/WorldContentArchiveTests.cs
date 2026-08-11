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

        Assert.Equal(before.AvailableQuests.Quests.Count - cleared.Length, after.AvailableQuests.Quests.Count);
        Assert.DoesNotContain(after.AvailableQuests.Quests, quest => cleared.Contains(quest.QuestId));
    }

    [SkippableFact]
    public async Task Two_jobs_are_offered_different_quest_sets()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var service = CreateService(scope);

        var swordman = await service.LoadAsync(Subject(level: 40, job: 0), CancellationToken.None);
        var mage = await service.LoadAsync(Subject(level: 40, job: 3), CancellationToken.None);

        Assert.NotEqual(
            swordman.AvailableQuests.Quests.Select(quest => quest.QuestId),
            mage.AvailableQuests.Quests.Select(quest => quest.QuestId));
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

    [SkippableFact]
    public async Task A_creature_with_a_script_yields_a_welcome_and_one_without_yields_none()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var creatures = scope.GetRequiredService<ICreatureDefinitionSource>();
        var all = await creatures.GetAllAsync(CancellationToken.None);
        var scripted = all.First(creature => creature.HasAmbientLines);
        var unscripted = all.First(creature => !creature.HasAmbientLines);

        var service = CreateService(scope);
        var withScript = await service.LoadAsync(
            Subject(equippedCreatureId: scripted.CreatureId),
            CancellationToken.None);
        var withoutScript = await service.LoadAsync(
            Subject(equippedCreatureId: unscripted.CreatureId),
            CancellationToken.None);

        Assert.NotNull(withScript.PetWelcome);
        Assert.NotEmpty(withScript.PetWelcome!.Lines);
        Assert.Null(withoutScript.PetWelcome);
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
        var placement = new EntryPlacementService(
            scope.GetRequiredService<ITownDefinitionSource>(),
            ChannelEntryOptions.Legacy);

        var result = await placement.ResolveAsync(
            new ChannelEntryIdentity(100, 10161, ChannelEntryKind.LevelRestricted),
            persistedTownId: 1,
            CancellationToken.None);

        Assert.True(result.Spawn.IsTransient);
        Assert.False(result.PersistsPosition);
        Assert.Equal(17, result.Spawn.TownId);
        Assert.Equal(4, result.Spawn.AreaId);
        Assert.Equal(378, result.Spawn.X);
        Assert.Equal(278, result.Spawn.Y);
    }

    [SkippableFact]
    public async Task An_ordinary_arrival_resolves_against_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var placement = new EntryPlacementService(
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

    private static WorldContentEntryService CreateService(IServiceProvider provider) =>
        new(provider.GetRequiredService<IQuestDefinitionSource>(),
            provider.GetRequiredService<IDailyActivityDefinitionSource>(),
            provider.GetRequiredService<ICollectionBoxDefinitionSource>(),
            provider.GetRequiredService<ICreatureDefinitionSource>());

    private static WorldContentSubject Subject(
        int level = 1,
        byte job = 0,
        byte growType = 0,
        IReadOnlyCollection<int>? clearedQuestIds = null,
        IReadOnlyList<CollectionBoxProgressSubject>? collectionBoxes = null,
        int equippedCreatureId = 0) =>
        new(level,
            job,
            growType,
            clearedQuestIds ?? [],
            [],
            collectionBoxes ?? [],
            equippedCreatureId);
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
