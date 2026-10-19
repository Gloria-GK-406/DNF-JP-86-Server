using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads what the archive declares about using an item.
/// </summary>
/// <remarks>
/// Use-related tags are optional and appear in no fixed order -- the reader is rewound for each because
/// <c>[minimum level]</c> sits before <c>[stackable type]</c> in one file and after it in the next.
///
/// Bracketed values such as <c>[all]</c> and <c>[village]</c> arrive as quoted strings, not as
/// identifiers; only real tags and their closing forms are identifiers. That is what lets a section
/// be read by taking values until the next identifier, and it is worth stating because the reverse
/// assumption reads every enum value as the start of a new section and finds every section empty.
/// </remarks>
internal sealed class PvfItemUseRuleDefinitionSource(PvfArchiveProvider provider)
    : IItemUseRuleDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableTypeField = PvfField.Custom<IReadOnlyList<PvfToken>>("[stackable type]",
            (_, values, source) =>
            {
                if (values.Count < 1 || !values[0].IsString ||
                    values.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32 || value.Value < 0))
                    throw new InvalidDataException($"PVF '{source}', field '[stackable type]': expected type name and complete nonnegative Int32 operands.");
                return values;
            }, optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ActionTypeField =
            PvfField.Tokens("[action type]", optional: true);
        internal static readonly PvfField<int> SharedEffectField = PvfField.Int32("[shared effect]", optional: true);
        internal static readonly PvfField<int> CoolTimeField = PvfField.Int32("[cool time]", optional: true);
        internal static readonly PvfField<int> CooltimeGroupField = PvfField.Int32("[cooltime group]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<double> ExpBonusRateField = PvfField.Single("[exp bonus rate]", optional: true);
        internal static readonly PvfField<int> StatChangeDurationField = PvfField.Custom<int>("[stat change duration]", (archive, values, source) =>
        {
            if (values.Count is < 1 or > 2 || values[0].Kind != PvfTokenKind.Int32
                || values.Count == 2 && !values[1].IsString)
                throw new InvalidDataException($"PVF '{source}', field '[stat change duration]': expected Int32 and optional target string.");
            return values[0].Value;
        }, optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> UsableJobField = PvfField.Strings("[usable job]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> ActionUsablePlaceField = PvfField.Strings("[action usable place]", optional: true);
    }

    private const string StackableListPath = "stackable/stackable.lst";
    private const string UnlimitedTypePrefix = "[unlimited";

    private static readonly Dictionary<string, ItemUsePlace> PlaceTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["[village]"] = ItemUsePlace.Village,
            ["[dungeon]"] = ItemUsePlace.Dungeon,
            ["[seria room]"] = ItemUsePlace.SeriaRoom,
            ["[tournament]"] = ItemUsePlace.Tournament,
        };

    private readonly Lock gate = new();
    private readonly Dictionary<int, ItemUseRule?> cache = [];
    private IReadOnlyDictionary<int, (int Id, string RelativePath)>? stackables;

    public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var cached))
                return ValueTask.FromResult(cached);

            var archive = provider.Archive;
            stackables ??= PvfListFile.Read(archive, StackableListPath).ToDictionary(value => value.Id);

            var rule = stackables.TryGetValue(itemId, out var entry)
                ? Read(archive, "stackable/" + entry.RelativePath)
                : null;
            cache[itemId] = rule;
            return ValueTask.FromResult(rule);
        }
    }

    private static ItemUseRule Read(PvfArchive archive, string path)
    {
        var reader = archive.OpenScript(path);

        var unreadable = false;
        var stackableType = TryReadFirstString(
                archive, ref reader, Fields.StackableTypeField, out var typeToken, ref unreadable)
            ? archive.ResolveString(typeToken.Value)
            : null;
        var summonsApc = PvfDocument.Open(archive, path).AtRoot().TryRead(PvfField.Tokens("[summon apc]", optional: true), out var summoned) && summoned.Count > 0;
        var action = TryReadFirstString(
                archive, ref reader, Fields.ActionTypeField, out var actionToken, ref unreadable)
            ? archive.ResolveString(actionToken.Value)
            : null;
        var shared = FirstInt32(archive, ref reader, Fields.SharedEffectField, ref unreadable);
        var cooldown = FirstInt32(archive, ref reader, Fields.CoolTimeField, ref unreadable);
        var cooltimeGroup = FirstInt32(archive,
            ref reader, Fields.CooltimeGroupField, ref unreadable);
        var minimumLevel = FirstInt32(archive,
            ref reader, Fields.MinimumLevelField, ref unreadable);
        var expBonusRate = FirstSingle(archive, ref reader, Fields.ExpBonusRateField, ref unreadable);
        var effectDuration = FirstInt32(archive,
            ref reader, Fields.StatChangeDurationField, ref unreadable);
        if (expBonusRate > 0 && effectDuration <= 0)
            unreadable = true;
        var jobText = AllStrings(archive, ref reader, Fields.UsableJobField, ref unreadable);
        var placeText = AllStrings(archive, ref reader, Fields.ActionUsablePlaceField, ref unreadable);

        return new ItemUseRule(
            IsConsumedOnUse: stackableType is null
                || !stackableType.StartsWith(UnlimitedTypePrefix, StringComparison.OrdinalIgnoreCase),
            CooldownMilliseconds: Math.Max(0, cooldown),
            CooltimeGroup: Math.Max(0, cooltimeGroup),
            MinimumLevel: Math.Max(0, minimumLevel),
            Jobs: ProjectJobs(jobText),
            Places: ProjectPlaces(placeText),
            HasUnreadableDeclaration: unreadable,
            ExpBonusRate: expBonusRate,
            EffectDurationMilliseconds: Math.Max(0, effectDuration),
            Action: action) { SummonsApc = summonsApc, SharedEffectSelector = shared == 1 && stackableType is "[only effect]" or "[random reward item]" ? (byte)0 : null };
    }

    /// <summary>
    /// An absent declaration is no restriction; a declaration that names something unknown, or that
    /// names nothing at all, is an invalid one -- which the rule reports as unreadable rather than
    /// as permission.
    /// </summary>
    /// <param name="tokens">
    /// Null when the archive does not declare the tag. An empty list means it declares it and gives
    /// no value, which is a different answer: the archive meant to restrict something and the
    /// restriction cannot be read. Folding the two together is how an item the archive withheld
    /// becomes an item open to everyone.
    /// </param>
    internal static ItemJobRestriction ProjectJobs(IReadOnlyList<string>? tokens)
    {
        if (tokens is null)
            return ItemJobRestriction.Any;
        if (tokens.Count == 1 && string.Equals(tokens[0], "[all]", StringComparison.OrdinalIgnoreCase))
            return ItemJobRestriction.Any;

        var families = tokens
            .Select(ArchiveJobFamilyTokens.Resolve)
            .Where(family => family is not QuestJobRequirement.Any)
            .Distinct()
            .ToArray();
        return new ItemJobRestriction(IsExactAll: false, families);
    }

    /// <inheritdoc cref="ProjectJobs" path="/param[@name='tokens']"/>
    internal static ItemPlaceRestriction ProjectPlaces(IReadOnlyList<string>? tokens)
    {
        if (tokens is null)
            return ItemPlaceRestriction.Anywhere;

        var places = tokens
            .Select(token => PlaceTokens.TryGetValue(token, out var place) ? place : ItemUsePlace.Unrecognized)
            .Distinct()
            .ToArray();
        return new ItemPlaceRestriction(IsUnrestricted: false, places);
    }

    /// <summary>
    /// The declared integer, or zero when the tag is absent.
    /// </summary>
    /// <param name="unreadable">
    /// Set when the tag is there and no integer follows it. Left alone when the tag is absent,
    /// because an undeclared cooldown is a real answer and an unreadable one is not.
    /// </param>
    // Typed callers validate the complete scalar declaration and distinguish absence from corruption.
    internal static int FirstInt32(PvfArchive archive, ref PvfTokenReader reader,
        PvfField<int> field, ref bool unreadable)
    {
        var values = RootSectionValues(archive, ref reader, field, out var duplicate, out _);
        if (values is null) return 0;
        try
        {
            if (duplicate) throw new InvalidDataException($"Duplicate {field.Name}.");
            return field.Decode(archive, values, "item use rule");
        }
        catch (InvalidDataException) { unreadable = true; return 0; }
    }

    internal static double FirstSingle(PvfArchive archive, ref PvfTokenReader reader,
        PvfField<double> field, ref bool unreadable)
    {
        var values = RootSectionValues(archive, ref reader, field, out var duplicate, out _);
        if (values is null) return 0;
        try
        {
            if (duplicate) throw new InvalidDataException($"Duplicate {field.Name}.");
            var value = field.Decode(archive, values, "item use rule");
            if (value > 0) return value;
        }
        catch (InvalidDataException) { }
        unreadable = true;
        return 0;
    }

    internal static bool TryReadFirstString(PvfArchive archive, ref PvfTokenReader reader,
        PvfField<IReadOnlyList<PvfToken>> field, out PvfToken value, ref bool unreadable)
    {
        value = default;
        var values = RootSectionValues(archive, ref reader, field, out var duplicate, out var firstRecord);
        if (values is null) return false;
        try
        {
            if (duplicate) throw new InvalidDataException($"Duplicate {field.Name}.");
            var decoded = field.Decode(archive, values, "item use rule");
            if (decoded.Count == 0 || !decoded[0].IsString) throw new InvalidDataException($"Missing name in {field.Name}.");
            // ActionType has a dedicated operand parser. Restore the cursor after the root name
            // without imposing the stackable type's integer-tail shape on action operands.
            reader.Seek(firstRecord);
            return reader.TryReadSectionValue(out value);
        }
        catch (InvalidDataException) { unreadable = true; return false; }
    }

    private static IReadOnlyList<PvfToken>? RootSectionValues(PvfArchive archive, ref PvfTokenReader reader,
        PvfField field, out bool duplicate, out int firstRecord)
    {
        reader.Reset();
        var sections = new List<(string Name, List<PvfToken> Values, int FirstRecord)>();
        List<PvfToken>? current = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier && archive.ResolveString(token.Value).StartsWith('['))
            {
                current = [];
                sections.Add((archive.ResolveString(token.Value), current, reader.Position / PvfToken.Size));
            }
            else current?.Add(token);
        }
        var closingNames = sections.Where(section => section.Name.StartsWith("[/", StringComparison.Ordinal))
            .Select(section => section.Name).ToHashSet(StringComparer.Ordinal);
        var blocks = new Stack<string>();
        IReadOnlyList<PvfToken>? values = null;
        duplicate = false; firstRecord = 0;
        foreach (var section in sections)
        {
            if (blocks.TryPeek(out var expected) && section.Name == expected) { blocks.Pop(); continue; }
            if (blocks.Count == 0 && field.Matches(section.Name))
            {
                duplicate |= values is not null;
                if (values is null) { values = section.Values; firstRecord = section.FirstRecord; }
            }
            var closing = "[/" + section.Name[1..];
            if (closingNames.Contains(closing)) blocks.Push(closing);
        }
        return values;
    }

    private static IReadOnlyList<PvfToken>? SectionValues(ref PvfTokenReader reader,
        TagRef tag, out bool duplicate)
    {
        reader.Reset();
        duplicate = false;
        if (!tag.IsResolved) return null;
        List<PvfToken>? values = null;
        var accepting = false;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                accepting = token.Value == tag.Offset;
                if (accepting)
                {
                    duplicate |= values is not null;
                    values ??= [];
                }
            }
            else if (accepting) values!.Add(token);
        }
        return values;
    }

    internal static int FirstInt32(ref PvfTokenReader reader, TagRef tag, ref bool unreadable)
    {
        var values = SectionValues(ref reader, tag, out var duplicate);
        if (values is null) return 0;
        if (!duplicate && values.Count == 1 && values[0].Kind == PvfTokenKind.Int32)
            return values[0].Value;
        unreadable = true;
        return 0;
    }

    /// <summary>
    /// The first string token of the section, left unresolved.
    /// </summary>
    /// <remarks>
    /// The name-table lookup stays with the caller so that deciding whether a declaration is
    /// readable needs no archive. That decision is the part worth holding down with a test, and the
    /// archive contains no item that exercises it -- every one of its 34,542 stackables parses.
    /// </remarks>
    /// <inheritdoc cref="FirstInt32" path="/param[@name='unreadable']"/>
    internal static bool TryReadFirstString(
        ref PvfTokenReader reader, TagRef tag, out PvfToken value, ref bool unreadable)
    {
        value = default;
        reader.Reset();
        if (!tag.IsResolved || !reader.TryMovePast(tag))
            return false;

        while (reader.TryReadSectionValue(out var token))
        {
            if (!token.IsString)
                continue;

            value = token;
            return true;
        }

        unreadable = true;
        return false;
    }

    /// <summary>
    /// Every value of the section, or null when the archive does not declare the tag.
    /// </summary>
    /// <remarks>
    /// The distinction between "not declared" and "declared with nothing in it" has to survive this
    /// far: only the caller knows that the first is permission and the second is a restriction it
    /// cannot read.
    /// </remarks>
    private static IReadOnlyList<string>? AllStrings(
        PvfArchive archive, ref PvfTokenReader reader, PvfField<IReadOnlyList<string>> field,
        ref bool unreadable)
    {
        var values = RootSectionValues(archive, ref reader, field, out var duplicate, out _);
        if (values is null) return null;
        try
        {
            if (duplicate) throw new InvalidDataException($"Duplicate {field.Name}.");
            return field.Decode(archive, values, "item use rule");
        }
        catch (InvalidDataException) { unreadable = true; return []; }
    }
}
