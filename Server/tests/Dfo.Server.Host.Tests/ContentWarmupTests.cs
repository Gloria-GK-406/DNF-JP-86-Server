using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.GameServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// Startup content warmup (spec RQ-091, appendix B "启动预热"). W1 runs inside S1's real-content host
/// (<see cref="AssertWarmedAsync"/>); W2 is structural; F1 pins the failure semantics.
/// </summary>
public sealed partial class ContentWarmupTests
{
    /// <summary>The warmup participants a real-content host registers. Appendix B cites this list.</summary>
    internal static readonly IReadOnlyList<string> Participants =
    [
        "achievement-catalog", "title-book-catalog", "character-skill-rules", "gold-carry-limits",
        "premium-service-rules", "quest-helper-puppets", "experience-rules", "drop-rules",
        "gold-silver-horn", "hell-reward", "sao-tower", "marriage-ceremony", "marriage-proposal",
        "marriage-capsule", "marriage-housing", "altar-dungeons", "joust", "tournament-dungeons",
        "initial-equipment", "initial-skills", "item-placement-index", "despair-tower", "towns",
        "daily-activities", "collection-boxes", "quests", "dungeon-index", "dungeon-map-index",
        "dungeon-room-object-index", "dungeon-room-actor-index", "epic-book-rules",
        // Whole indexes executor jobs reach outside the synthetic load (review B2).
        "creatures", "creature-equipment-lifetimes", "creature-rules", "cycle-dungeons", "equipment-reseal",
        "mirror-dungeons", "quest-parameters", "inventory-expansion", "item-catalog-index", "equipment-forging",
        "equipment-outworld", "illusion-towers", "drop-rate-tables", "clear-reward-rates", "avatar-compound",
        "cargo-item-index", "cera-shop", "equipment-disjoint", "equipment-enchant-index", "equipment-magic-seal-index",
        "equipment-magic-seal-grades", "equipment-quality-index", "equipment-reinforcement", "equipment-repair",
        "equipment-requirement-index", "experience-item-index", "inventory-sort-index", "item-sale-index",
        "item-socket-index", "item-usage-index", "item-use-index", "mail-item-index", "npc-shop-index", "recipe-index",
        "stackable-action-index", "teleport-consumable-index", "tutorial-rewards", "lucky-star-rental",
        "equipment-slot-index", "hell-dungeons",
    ];

    /// <summary>
    /// Every Application/Infrastructure type holding a <see cref="Lazy{T}"/> or a <c>??=</c> first-use index,
    /// and how startup covers it. <c>Via</c> null: the holder is itself a participant. <c>Via</c> "startup:":
    /// an item StartingAsync already warmed before this unit. <c>Service</c>: how W1 reaches the instance.
    /// W2 does not see first-use indexes written as <c>if (x is null)</c>, hand-filled dictionaries or
    /// <c>is {}</c> checks; those are covered by the participant list and the reachability table in TASK-15.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Holder> Holders = new Dictionary<string, Holder>
    {
        [Infra("PvfAchievementDefinitionSource")] = Warmed<Abstractions.Achievements.IAchievementDefinitionSource>(),
        [Infra("PvfTitleBookDefinitionSource")] = Warmed<Abstractions.Achievements.ITitleBookDefinitionSource>(),
        [App("Characters.CharacterSkillRuleService")] = Warmed<Abstractions.Characters.ICharacterSkillRuleService>("CharacterSkillRuleWarmup"),
        [Infra("PvfSkillPointDefinitionSource")] = Warmed<ISkillRuleDefinitionSource>("CharacterSkillRuleWarmup"),
        [Infra("PvfGoldCarryLimitSource")] = Warmed<IGoldCarryLimitSource>(),
        [App("Accounts.PremiumServiceRuleService")] = Warmed<Abstractions.Accounts.IPremiumServiceRuleService>(),
        [App("Accounts.QuestHelperPuppetRuleService")] = Warmed<Abstractions.Accounts.IQuestHelperPuppetRuleService>(),
        [App("World.ExperienceRuleService")] = Warmed<Abstractions.World.IExperienceRuleService>(),
        [Infra("PvfExperienceParameterSource")] = Warmed<IExperienceParameterSource>("ExperienceRuleService"),
        [Infra("PvfDungeonRankGradeSource")] = Warmed<IDungeonRankGradeSource>("ExperienceRuleService"),
        [Infra("ChannelScriptExperienceSource")] = Warmed<Abstractions.Channel.IChannelExperienceSource>("ExperienceRuleService"),
        [Infra("PvfDropItemPoolSource")] = Warmed<IDropItemPoolSource>("DropRuleService"),
        [Infra("PvfGoldSilverHornContentSource")] = Warmed<Abstractions.World.IGoldSilverHornContentSource>(),
        [Infra("PvfHellRewardDefinitionSource")] = Warmed<Abstractions.World.IHellRewardDefinitionSource>(),
        [Infra("PvfSaoTowerContentSource")] = Warmed<Abstractions.World.ISaoTowerContentSource>(),
        [Infra("PvfMarriageCeremonyContentSource")] = Warmed<Abstractions.Marriage.IMarriageCeremonyContentSource>(),
        [Infra("PvfMarriageProposalContentSource")] = Warmed<Abstractions.Marriage.IMarriageProposalContentSource>(),
        [Infra("PvfMarriageCapsuleContentSource")] = Warmed<Abstractions.Marriage.IMarriageCapsuleContentSource>(),
        [Infra("PvfMarriageHousingContentSource")] = Warmed<IMarriageHousingContentSource>(),
        [Infra("PvfItemPlacementDefinitionSource")] = Warmed<IItemPlacementDefinitionSource>(),
        [Infra("PvfTownDefinitionSource")] = Warmed<ITownDefinitionSource>(),
        [Infra("PvfDailyActivityDefinitionSource")] = Warmed<IDailyActivityDefinitionSource>(),
        [Infra("PvfCollectionBoxDefinitionSource")] = Warmed<ICollectionBoxDefinitionSource>(),
        [Infra("PvfQuestDefinitionSource")] = Warmed<IQuestDefinitionSource>(),
        [Infra("PvfDungeonMapDefinitionSource")] = Warmed<IDungeonMapDefinitionSource>(),
        [Infra("PvfPackageOpeningDefinitionSource")] = Warmed<IPackageOpeningDefinitionSource>(
            "startup: lottery definitions build paths; premiums through JoustRuleService"),
        [Infra("PvfCeraShopDefinitionSource")] = Warmed<ICeraShopDefinitionSource>(),
        [App("Inventory.EpicBookRuleService")] = Warmed<Abstractions.Inventory.IEpicBookRuleService>(),
        [Infra("PvfEpicBookDefinitionSource")] = Warmed<Abstractions.Inventory.IEpicBookDefinitionSource>("EpicBookRuleService"),
        [Infra("PvfAvatarCompoundDefinitionSource")] = Warmed<IAvatarCompoundDefinitionSource>(),
        [Infra("PvfCargoItemDefinitionSource")] = Warmed<ICargoItemDefinitionSource>(),
        [Infra("PvfCreatureDefinitionSource")] = Warmed<ICreatureDefinitionSource>(),
        [Infra("PvfCreatureEquipmentLifetimeSource")] = Warmed<ICreatureEquipmentLifetimeSource>(),
        [Infra("PvfCreatureRuleSource")] = Warmed<Abstractions.Creatures.ICreatureRuleSource>(),
        [Infra("PvfCycleDungeonDefinitionSource")] = Warmed<Abstractions.World.ICycleDungeonDefinitionSource>(),
        [Infra("PvfEquipmentDisjointDefinitionSource")] = Warmed<IEquipmentDisjointDefinitionSource>(),
        [Infra("PvfEquipmentEnchantContentSource")] = Warmed<IEquipmentEnchantContentSource>(),
        [Infra("PvfEquipmentMagicSealContentSource")] = Warmed<IEquipmentMagicSealContentSource>(),
        [Infra("PvfEquipmentMagicSealGradeDefinitionSource")] = Warmed<IEquipmentMagicSealGradeDefinitionSource>(),
        [Infra("PvfEquipmentQualityDefinitionSource")] = Warmed<IEquipmentQualityDefinitionSource>(),
        [Infra("PvfEquipmentReinforcementDefinitionSource")] = Warmed<IEquipmentReinforcementDefinitionSource>(),
        [Infra("PvfEquipmentRepairDefinitionSource")] = Warmed<IEquipmentRepairDefinitionSource>(),
        [Infra("PvfEquipmentRequirementDefinitionSource")] = Warmed<IEquipmentRequirementDefinitionSource>(),
        [Infra("PvfExperienceItemDefinitionSource")] = Warmed<IExperienceItemDefinitionSource>(),
        [Infra("PvfHellDungeonDefinitionSource")] = Warmed<Abstractions.World.IHellDungeonDefinitionSource>(),
        [Infra("PvfInventorySortKeySource")] = Warmed<Abstractions.Inventory.IInventorySortKeySource>(),
        [Infra("PvfItemSaleDefinitionSource")] = Warmed<IItemSaleDefinitionSource>(),
        [Infra("PvfItemSocketDefinitionSource")] = Warmed<IItemSocketDefinitionSource>(),
        [Infra("PvfItemUsageRuleSource")] = Warmed<IItemUsageRuleSource>(),
        [Infra("PvfItemUseRuleDefinitionSource")] = Warmed<IItemUseRuleDefinitionSource>(),
        [Infra("PvfLuckyStarRentalContentSource")] = Warmed<Abstractions.Rental.ILuckyStarRentalContentSource>(),
        [Infra("PvfMailItemDefinitionSource")] = Warmed<IMailItemDefinitionSource>(),
        [Infra("PvfMirrorDungeonDefinitionSource")] = Warmed<Abstractions.World.IMirrorDungeonDefinitionSource>(),
        [Infra("PvfNpcShopDefinitionSource")] = Warmed<INpcShopDefinitionSource>(),
        [Infra("PvfQuestParameterSource")] = Warmed<IQuestParameterSource>(),
        [Infra("PvfRecipeContentSource")] = Warmed<Abstractions.Inventory.IRecipeContentSource>(),
        [Infra("PvfStackableActionDefinitionSource")] = Warmed<IStackableActionDefinitionSource>(),
        [Infra("PvfTeleportConsumableDefinitionSource")] = Warmed<Abstractions.Inventory.ITeleportConsumableDefinitionSource>(),
        [Infra("PvfTutorialRewardDefinitionSource")] = Warmed<ITutorialRewardDefinitionSource>(),
        // Warmed by StartingAsync items that predate this unit.
        [Infra("PvfExtraDropContentSource")] = Warmed<IExtraDropContentSource>("startup: extra drop content"),
        [App("World.ExtraDropRuleService")] = Warmed<IExtraDropRuleService>("startup: extra drop content"),
        [Infra("PvfDimensionDungeonContentSource")] = Warmed<Abstractions.World.IDimensionDungeonContentSource>("startup: dimension dungeon content"),
        [App("World.DimensionDungeonRuleService")] = Warmed<Abstractions.World.IDimensionDungeonRuleService>("startup: dimension dungeon content"),
        [App("World.IndependentDropRuleService")] = Warmed<IIndependentDropRuleService>("startup: independent drops"),
        [Infra("PvfEquipmentDefinitionSource")] = Warmed<IEquipmentDefinitionSource>("startup: equipment content index"),
        // Not executor content caches.
        [Infra("PvfArchiveProvider")] = Exempt("the archive itself; every warmup item opens it first"),
        [App("Effects.InventoryEffectPreflightDomainService+Standing")] = Exempt("a value computed inside one preflight call, not a content cache"),
        [Infra("PvfEntryLifecycleContentSource")] = Exempt("slotMappings is a rule object built without reading the archive"),
        [Infra("PvfItemTooltipSource")] = Exempt("read only by GET /api/gm/v1/items/{itemId}/tooltip, a route outside gameplay (appendix B)"),
    };

    [Fact]
    public void W2_every_lazy_or_first_use_index_holder_is_warmed_or_exempt_with_a_reason()
    {
        var violations = Violations(FindHolders(ProductionTypes(), ProductionSources()), Holders);
        Assert.True(violations.Count == 0, string.Join('\n', violations));
    }

    [Fact]
    public void W2_rejects_an_unlisted_lazy_field_and_an_unlisted_first_use_index()
    {
        var found = FindHolders([typeof(UnlistedLazyHolder), typeof(UnlistedIndexHolder)],
            [("UnlistedIndexHolder.cs", "sealed class UnlistedIndexHolder\n{\n    public int[] Index => index ??= [1];\n}\n")]);

        var violations = Violations(found, new Dictionary<string, Holder>());

        Assert.Contains(violations, v => v.Contains(typeof(UnlistedLazyHolder).FullName!) && v.Contains("value"));
        Assert.Contains(violations, v => v.Contains(typeof(UnlistedIndexHolder).FullName!) && v.Contains("index"));
    }

    [Fact]
    public async Task F1_a_failing_item_is_logged_and_the_rest_still_warm()
    {
        var log = new Capture();
        var first = new Participant("first", () => 3);
        var broken = new Participant("broken", () => throw new InvalidOperationException("bad content"));
        var last = new Participant("last", () => 5);

        await ContentWarmup.RunAsync(true, () => [first, broken, last], log, CancellationToken.None);

        Assert.Equal([1, 1, 1], new[] { first.Calls, broken.Calls, last.Calls });
        var failure = Assert.Single(log.Entries, entry => entry.Message.StartsWith("Content warmup failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, failure.Level);
        Assert.Contains("broken", failure.Message, StringComparison.Ordinal);
        Assert.Equal("bad content", Assert.IsType<InvalidOperationException>(failure.Error).Message);
        Assert.Contains(log.Entries, entry => entry.Message.StartsWith("Content warmup finished: 3 items, 1 failed,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task F1_cancellation_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var participant = new Participant("only", () => 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ContentWarmup.RunAsync(true, () => [participant], new Capture(), cancelled.Token));
        Assert.Equal(0, participant.Calls);
    }

    [Fact]
    public async Task Switched_off_the_warmup_resolves_and_calls_no_participant()
    {
        var log = new Capture();
        var participant = new Participant("only", () => 1);
        var resolved = false;

        var items = await ContentWarmup.RunAsync(false, () => { resolved = true; return [participant]; }, log, CancellationToken.None);

        Assert.Equal(0, items);
        Assert.False(resolved);
        Assert.Equal(0, participant.Calls);
        var skipped = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, skipped.Level);
        Assert.StartsWith("Content warmup skipped by configuration", skipped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_participant_replaced_by_a_double_is_reported_and_not_warmed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReplaceableSource>(new ReplacementDouble());
        services.AddContentWarmup<IReplaceableSource>();
        await using var provider = services.BuildServiceProvider();
        var log = new Capture();

        var items = await ContentWarmup.RunAsync(true, provider.GetServices<IContentWarmup>, log, CancellationToken.None);

        Assert.Equal(1, items);
        var skipped = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.StartsWith("Content warmup skipped", skipped.Message, StringComparison.Ordinal);
        Assert.Contains(log.Entries, entry => entry.Message.StartsWith("Content warmup finished: 1 items, 0 failed,", StringComparison.Ordinal));
    }

    [Fact]
    public void The_switch_defaults_to_on_and_only_an_explicit_false_turns_it_off()
    {
        static IConfiguration With(string? value) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ContentWarmup.EnabledKey] = value }).Build();

        Assert.True(ContentWarmup.IsEnabled(null));
        Assert.True(ContentWarmup.IsEnabled(With(null)));
        Assert.True(ContentWarmup.IsEnabled(With("true")));
        Assert.False(ContentWarmup.IsEnabled(With("false")));
        Assert.Throws<InvalidOperationException>(() => ContentWarmup.IsEnabled(With("sometimes")));
    }

    /// <summary>
    /// W1: called by S1 once its real-content host has finished StartingAsync. Every participant is
    /// registered and answers with entries; every warmed holder's first-use fields are loaded, keyed
    /// caches over every key of their bounded key set.
    /// </summary>
    internal static async Task AssertWarmedAsync(IServiceProvider services, IEnumerable<string> hostLog)
    {
        var warmups = services.GetServices<IContentWarmup>().ToArray();
        Assert.Equal(Participants.Order(StringComparer.Ordinal), warmups.Select(w => w.Name).Order(StringComparer.Ordinal));

        // StartingAsync ran the warmup exactly once, every item succeeded.
        var log = hostLog.ToArray();
        Assert.Single(log, line => line.StartsWith($"Content warmup finished: {Participants.Count} items, 0 failed,", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.StartsWith("Content warmup failed", StringComparison.Ordinal)
            || line.StartsWith("Content warmup skipped", StringComparison.Ordinal));

        // Fields first: this is the state the host started with, before this check touches any participant.
        AssertHoldersLoaded(services);
        foreach (var warmup in warmups)
            Assert.True(await warmup.WarmAsync(CancellationToken.None) > 0, warmup.Name);
    }

    private static void AssertHoldersLoaded(IServiceProvider services)
    {
        var found = FindHolders(ProductionTypes(), ProductionSources());
        foreach (var (type, fields) in found)
        {
            if (!Holders.TryGetValue(type.FullName!, out var holder) || holder.Service is null) continue;
            var instance = Locate(services.GetRequiredService(holder.Service), type)
                ?? throw new Xunit.Sdk.XunitException($"{type.FullName} is not reachable from {holder.Service.Name}");
            foreach (var field in fields)
            {
                var value = field.GetValue(instance);
                Assert.True(IsLoaded(value), $"{type.Name}.{field.Name} was not loaded at startup");
                if (KeySets(services).TryGetValue((type.FullName!, field.Name), out var keys))
                    Assert.Superset(keys.ToHashSet(), ((IDictionary)value!).Keys.Cast<object>().ToHashSet());
            }
        }
    }

    private static Dictionary<(string, string), IEnumerable<object>> KeySets(IServiceProvider services)
    {
        var jobs = services.GetRequiredService<Abstractions.Characters.ICharacterStatRuleService>().SupportedCareers
            .Select(career => (object)career.Job).Distinct().ToArray();
        Assert.NotEmpty(jobs);
        return new()
        {
            [(App("Characters.CharacterSkillRuleService"), "jobs")] = jobs,
            [(Infra("PvfSkillPointDefinitionSource"), "catalogs")] = jobs,
            [(Infra("PvfDropItemPoolSource"), "_pools")] = Enum.GetValues<DropItemFamily>().Cast<object>().ToArray(),
        };
    }

    private static bool IsLoaded(object? value)
    {
        if (value is null) return false;
        var type = value.GetType();
        if (IsLazy(type)) return (bool)type.GetProperty("IsValueCreated")!.GetValue(value)!;
        if (value is IDictionary dictionary && IsLazyDictionary(type))
            return dictionary.Count > 0 && dictionary.Values.Cast<object>().All(IsLoaded);
        return true;
    }

    private static object? Locate(object root, Type target, int depth = 2)
    {
        if (target.IsInstanceOfType(root)) return root;
        if (depth == 0) return null;
        foreach (var field in root.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.FieldType.IsValueType || field.GetValue(root) is not { } child) continue;
            if (Locate(child, target, depth - 1) is { } hit) return hit;
        }
        return null;
    }

    internal sealed record Holder(Type? Service, string? Via, string? Reason);

    private static Holder Warmed<TService>(string? via = null) => new(typeof(TService), via, null);
    private static Holder Exempt(string reason) => new(null, null, reason);
    private static string Infra(string name) => "Dfo.Server.Infrastructure.Content." + name;
    private static string App(string name) => "Dfo.Server.Application." + name;

    /// <summary>
    /// A holder is a type with an instance field that is a <see cref="Lazy{T}"/> (or a concurrent map of
    /// them), found by reflection, or a field assigned with <c>??=</c>, found in source: a <c>??=</c>
    /// target counts when a type declared in that file has an instance field of that name.
    /// </summary>
    internal static IReadOnlyDictionary<Type, IReadOnlyList<FieldInfo>> FindHolders(
        IEnumerable<Type> types, IEnumerable<(string Path, string Text)> sources)
    {
        var all = types.Where(type => !type.Name.Contains('<')).ToArray();
        var found = new Dictionary<Type, HashSet<FieldInfo>>();
        void Add(Type type, FieldInfo field)
        {
            if (!found.TryGetValue(type, out var set)) found[type] = set = [];
            set.Add(field);
        }
        foreach (var type in all)
            foreach (var field in InstanceFields(type).Where(field => IsLazy(field.FieldType) || IsLazyDictionary(field.FieldType)))
                Add(type, field);
        foreach (var (_, text) in sources)
        {
            var declared = DeclaredType().Matches(text).Select(match => match.Groups["name"].Value).ToHashSet();
            foreach (var name in Coalesce().Matches(text).Select(match => match.Groups["name"].Value).Distinct())
                foreach (var type in all.Where(type => declared.Contains(SimpleName(type))))
                    if (InstanceFields(type).FirstOrDefault(field => field.Name == name) is { } field)
                        Add(type, field);
        }
        return found.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<FieldInfo>)pair.Value.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyList<string> Violations(
        IReadOnlyDictionary<Type, IReadOnlyList<FieldInfo>> found, IReadOnlyDictionary<string, Holder> table)
    {
        var violations = new List<string>();
        var names = found.Keys.Select(type => type.FullName!).ToHashSet();
        foreach (var (type, fields) in found.OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal))
        {
            var what = $"{type.FullName} ({string.Join(", ", fields.Select(field => field.Name))})";
            if (!table.TryGetValue(type.FullName!, out var holder))
                violations.Add($"{what}: neither warmed nor exempt");
            else if (holder.Reason is { } reason && string.IsNullOrWhiteSpace(reason))
                violations.Add($"{what}: exempt without a reason");
            else if (holder.Reason is null && holder.Via is null && !typeof(IContentWarmup).IsAssignableFrom(type))
                violations.Add($"{what}: listed as a participant but does not implement IContentWarmup");
            else if (holder.Via is { } via && !via.StartsWith("startup:", StringComparison.Ordinal)
                     && !ParticipantTypes().Any(participant => participant.Name == via))
                violations.Add($"{what}: warmed via {via}, which is not an IContentWarmup participant");
        }
        violations.AddRange(table.Keys.Where(name => !names.Contains(name)).Select(name => $"{name}: listed but holds nothing"));
        return violations;
    }

    private static IEnumerable<Type> ParticipantTypes() =>
        ProductionTypes().Where(type => typeof(IContentWarmup).IsAssignableFrom(type) && !type.IsInterface);

    private static Type[] ProductionTypes() =>
        [.. Assembly.Load("Dfo.Server.Application").GetTypes(), .. Assembly.Load("Dfo.Server.Infrastructure").GetTypes()];

    private static IEnumerable<(string Path, string Text)> ProductionSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "86JP.Server.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        foreach (var project in new[] { "Dfo.Server.Application", "Dfo.Server.Infrastructure" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src", project), "*.cs", SearchOption.AllDirectories))
                if (!path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    yield return (path, File.ReadAllText(path));
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static bool IsLazy(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Lazy<>);

    private static bool IsLazyDictionary(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>) && IsLazy(type.GetGenericArguments()[1]);

    [GeneratedRegex(@"(?<![\w.])(?:this\.)?(?<name>_?[A-Za-z]\w*)\s*\?\?=")]
    private static partial Regex Coalesce();

    [GeneratedRegex(@"\b(?:class|record|struct)\s+(?<name>\w+)")]
    private static partial Regex DeclaredType();

    private sealed class UnlistedLazyHolder
    {
        private readonly Lazy<int> value = new(() => 1);
        public int Value => value.Value;
    }

    private sealed class UnlistedIndexHolder
    {
        private int[]? index;
        public int[] Index => index ??= [1];
    }

    private interface IReplaceableSource;

    private sealed class ReplacementDouble : IReplaceableSource;

    private sealed class Participant(string name, Func<int> warm) : IContentWarmup
    {
        public int Calls { get; private set; }
        public string Name => name;

        public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(warm());
        }
    }

    private sealed class Capture : ILogger
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
