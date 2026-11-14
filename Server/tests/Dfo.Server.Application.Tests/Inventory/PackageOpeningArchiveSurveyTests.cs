using System.Text;
using System.Text.RegularExpressions;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Surveys every ordinary, selection and avatar package in the archive: can its definition be read, and can
/// each declared reward be prepared? Failures are allowed only for content the archive itself lacks or for
/// declarations that stay refused by decision (#328); anything else is a regression.
/// Set PACKAGE_SURVEY_OUT to a file path to keep the per-package rows.
/// </summary>
public sealed class PackageOpeningArchiveSurveyTests(ITestOutputHelper output)
{
    // Content faults that #340 removes from the archive, and the [cera package] selection groups that only a
    // purchase can choose (#328 decision: such a package is never opened from the inventory).
    private static readonly string[] AcceptedReasons =
    [
        "R-missing", "R-prepare:Reward # has already expired.", "UnknownCreature", "D:Purchase-only selection groups",
    ];
    private const int Jobs = 14;

    // Packages that still fail, by decision, each named so a new failure cannot hide among them.
    private static readonly Dictionary<int, string> KnownResiduals = new[]
    {
        // Service rewards with no effect yet: premium [target item] terms (16, 137, 29), skill build unlock (821),
        // character slots (2660239, #327), devil contract components (2681929-2682206), 10012575 and a name tag.
        ("service", new[] { 27, 63, 64, 65, 66, 67, 69, 70, 71, 72, 73, 152, 719, 838, 957, 2660393, 2660394, 7523,
            2682006, 10007846, 10005185, 10153551, 10153887, 10012295, 10015568, 10148432, 10157325, 10158080,
            490001485, 490001928 }),
        // Declarations under review against the client on #340: an ability outside the avatar's options, fractional
        // job pool weights, conflicting rows for one category, and an unclosed selection block.
        ("#340", new[] { 54, 10003593, 490001689, 490001919 }),
    }.SelectMany(group => group.Item2.Select(id => (id, group.Item1))).ToDictionary(x => x.id, x => x.Item2);

    [SkippableFact]
    public async Task Every_archive_package_opens_or_fails_only_for_accepted_content_reasons()
    {
        var options = new PvfContentOptions();
        Skip.If(options.ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(options);
        using var shop = new CeraShopFixture(useArchive: true);
        var content = shop.Host.Services.GetRequiredService<IPackageOpeningDefinitionSource>();
        var items = shop.Host.Services.GetRequiredService<ICeraShopDefinitionSource>();
        var rules = shop.Host.Services.GetRequiredService<IPackageOpeningRuleService>();
        var now = DateTimeOffset.UtcNow;
        var rows = new StringBuilder();
        var reasons = new Dictionary<string, int>();
        var kinds = new Dictionary<OpeningPackageKind, (int Total, int Failed)>();
        var unexpected = new List<string>();
        var expanding = new SortedSet<int>();
        var failing = new HashSet<int>();
        var current = 0;
        foreach (var (id, path) in PvfListFile.Paths(provider.Archive, "stackable/stackable.lst", prefix: "stackable/"))
        {
            OpeningPackageDefinition? definition;
            try { definition = await content.GetAsync(id, default); }
            catch (Exception error) { Note(id, "?", [$"D-throws:{error.GetType().Name}"], path); continue; }
            if (definition is not { Kind: OpeningPackageKind.Package or OpeningPackageKind.Selection or OpeningPackageKind.AvatarPackage }) continue;
            current = id;
            var found = new List<string>(definition.Diagnostics.Select(d => "D:" + d));
            if (definition.PurchaseSelectionGroups > 0) found.Add("D:Purchase-only selection groups");
            var rewards = definition.FixedRewards.Concat(definition.Pools.SelectMany(p => p.Choices).Select(c => c.Reward))
                .Concat(definition.Categories.SelectMany(c => c.Rewards));
            foreach (var reward in rewards.DistinctBy(r => (r.ItemId, r.Count, r.Ability, r.PeriodDays, r.Upgrade, r.AvatarTypeRow)))
                found.AddRange(await RewardFaults(reward, 0));
            var (total, failed) = kinds.GetValueOrDefault(definition.Kind);
            kinds[definition.Kind] = (total + 1, failed + (found.Count == 0 ? 0 : 1));
            if (found.Count > 0) Note(id, definition.Kind.ToString(), found.Distinct().ToList(), path);
        }
        foreach (var (kind, (total, failed)) in kinds) output.WriteLine($"{kind}: {failed}/{total} fail");
        output.WriteLine($"{expanding.Count} packages hold a grant-time package: {string.Join(' ', expanding.Take(30))}");
        foreach (var (reason, count) in reasons.OrderByDescending(r => r.Value)) output.WriteLine($"{count,6}  {reason}");
        if (Environment.GetEnvironmentVariable("PACKAGE_SURVEY_OUT") is { Length: > 0 } file) File.WriteAllText(file, rows.ToString());
        var stale = KnownResiduals.Keys.Where(id => !failing.Contains(id)).ToArray();
        Assert.True(stale.Length == 0, $"Registered residuals now open; remove them: {string.Join(' ', stale)}");
        Assert.True(unexpected.Count == 0, $"{unexpected.Count} packages fail for other reasons:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unexpected.Take(40)));

        void Note(int id, string kind, List<string> found, string path)
        {
            failing.Add(id);
            rows.Append(id).Append('\t').Append(kind).Append('\t').Append(string.Join(" | ", found)).Append('\t').AppendLine(path);
            foreach (var reason in found.Select(Normalize).Distinct()) reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            if (!KnownResiduals.ContainsKey(id) && found.Any(r => !AcceptedReasons.Any(a => Normalize(r).Contains(a, StringComparison.Ordinal))))
                unexpected.Add($"{id} {kind} {path}: {string.Join(" | ", found)}");
        }

        async Task<IEnumerable<string>> RewardFaults(OpeningReward reward, int depth)
        {
            OpeningRewardItemDefinition? item;
            try { item = await content.GetRewardAsync(reward.ItemId, default); }
            catch (Exception error) { return [$"R-throws:{error.Message}"]; }
            if (item is null) return [$"R-missing {reward.ItemId}"];
            if (!item.Supported)
            {
                var declared = await items.GetItemAsync(reward.ItemId, default);
                return [$"R-unsupported:{string.Join(',', declared?.Diagnostics.Select(d => d.Code).Distinct() ?? [])}:{declared?.Kind} {reward.ItemId}"];
            }
            if (item.ExpandsWhenGranted)
            {
                // A [cera package] / [cera booster] reward is granted as its contents: survey what each job can draw.
                if (depth > 0) return [];
                expanding.Add(current);
                var faults = new List<string>();
                var parts = new List<OpeningReward>();
                for (var job = 0; job < Jobs; job++)
                {
                    try { parts.AddRange(await rules.ExpandAsync([reward with { Count = 1 }], job, job, default)); }
                    catch (OpeningRejectedException error) when (error.Reason == OpeningRejection.ItemUnusable) { }
                    catch (OpeningRejectedException error) { faults.Add($"R-expand:{error.Message}"); }
                }
                foreach (var part in parts.DistinctBy(r => (r.ItemId, r.Count, r.PeriodDays)))
                    faults.AddRange(await RewardFaults(part, depth + 1));
                return faults;
            }
            // The avatar window lets the opener choose a declared ability; a pool row names its own.
            if (item.Placement.WorldClass == ItemWorldClass.Avatar && reward.Ability == 0 && !item.Abilities.IsEmpty)
                reward = reward with { Ability = checked((byte)item.Abilities[0]) };
            if (reward.Upgrade == OpeningReward.RandomUpgrade) reward = reward with { Upgrade = reward.UpgradeChances[0].Level };
            try { await rules.PrepareAsync([reward], now, default); }
            catch (OpeningRejectedException error) { return [$"R-prepare:{error.Message}"]; }
            return [];
        }
    }

    private static string Normalize(string reason) => Regex.Replace(reason, @"\d+", "#");
}
