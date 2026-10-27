using Dfo.Pvf.Tokens;
using Dfo.Pvf.Schema;
using System.Globalization;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfStackableActionDefinitionSource(PvfArchiveProvider provider)
    : IStackableActionDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ActionTypeField = PvfField.Tokens("[action type]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<int> MaximumLevelField = PvfField.Int32("[maximum level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ExpirationDateField = PvfField.Tokens("[expiration date]", optional: true);
        internal static readonly PvfField<int> LevelupRangeField = PvfField.Int32("[levelup range]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> ImpossibleContentsField = PvfField.Strings("[impossible contents]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, StackableActionDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;

    public string Name => "stackable-action-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            PathsIndex(archive);
            return ValueTask.FromResult(paths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(paths))]
    private void PathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        paths ??= PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
    }

    public ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var found)) return ValueTask.FromResult(found);
            var archive = provider.Archive;
            PathsIndex(archive);
            found = paths.TryGetValue(itemId, out var path) ? Read(archive, path) : null;
            cache[itemId] = found;
            return ValueTask.FromResult(found);
        }
    }

    private static StackableActionDefinition? Read(PvfArchive archive, string path)
    {
        var reader = archive.OpenScript(path);
        var unreadable = false;
        if (!PvfItemUseRuleDefinitionSource.TryReadFirstString(archive, ref reader,
            Fields.ActionTypeField, out var action, ref unreadable)) return null;
        var actionName = archive.ResolveString(action.Value);
        var document = PvfDocument.Open(archive, path).AtRoot();
        if (actionName == "[add fatigue]")
        {
            var operands = document.Read(Fields.ActionTypeField);
            if (operands.Count != 2 || !operands[0].IsString || operands[1].Kind != PvfTokenKind.Int32)
                unreadable = true;
        }
        var recovery = 0;
        var entryDungeon = 0;
        var entryIncrease = 0;
        if (actionName == "[add enter limit dungeonincount]")
        {
            var operands = document.Read(Fields.ActionTypeField);
            if (operands.Count != 3 || !operands[0].IsString || operands[1].Kind != PvfTokenKind.Int32
                || operands[2].Kind != PvfTokenKind.Int32 || operands[1].Value <= 0 || operands[2].Value <= 0) unreadable = true;
            else { entryIncrease = operands[1].Value; entryDungeon = operands[2].Value; }
        }
        var increase = 1;
        var blockedInHardcore = false;
        if (actionName == "[level up ticket]")
        {
            var operands = document.Read(Fields.ActionTypeField);
            if (operands.Count != 1 || !operands[0].IsString) unreadable = true;
            if (document.TryRead(Fields.LevelupRangeField, out var range)) increase = range;
            if (increase <= 0) unreadable = true;
            if (document.TryRead(Fields.ImpossibleContentsField, out var restrictions))
            {
                blockedInHardcore = restrictions.Contains("hardcore mode", StringComparer.OrdinalIgnoreCase);
                if (restrictions.Any(x => !x.Equals("hardcore mode", StringComparison.OrdinalIgnoreCase))) unreadable = true;
            }
        }
        if (actionName == "[add fatigue]" && !reader.TryReadInt32(out recovery)) unreadable = true;
        var min = PvfItemUseRuleDefinitionSource.FirstInt32(archive, ref reader,
            Fields.MinimumLevelField, ref unreadable);
        var maximumTag = archive.GetTag(Fields.MaximumLevelField);
        reader.Reset();
        var max = maximumTag.IsResolved && reader.TryMovePast(maximumTag)
            ? PvfItemUseRuleDefinitionSource.FirstInt32(archive, ref reader, Fields.MaximumLevelField, ref unreadable)
            : int.MaxValue;
        long expiry = 0;
        if (document.TryRead(Fields.ExpirationDateField, out var dates))
        {
            if (dates.Count != 1) unreadable = true;
            else
            {
                var token = dates[0];
                if (token.IsString && DateTime.TryParseExact(archive.ResolveString(token.Value),
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    expiry = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUnixTimeSeconds();
                else if (token.Kind != PvfTokenKind.Int32 || token.Value != 0) unreadable = true;
            }
        }
        return new(actionName, min, max, expiry,
            !unreadable && min >= 0 && max >= min
                && (actionName != "[add fatigue]" || recovery is > 0 and <= ushort.MaxValue))
        { FatigueRecovery = recovery, LevelIncrease = increase, BlockedInHardcore = blockedInHardcore,
            EntryDungeonId = entryDungeon, EntryIncrease = entryIncrease };
    }
}
