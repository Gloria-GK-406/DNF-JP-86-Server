using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads what the archive declares about using an item.
/// </summary>
/// <remarks>
/// Six tags, all optional, in no fixed order -- the reader is rewound for each because
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
                ref reader, archive.GetTag("[stackable type]"), out var typeToken, ref unreadable)
            ? archive.ResolveString(typeToken.Value)
            : null;
        var cooldown = FirstInt32(ref reader, archive.GetTag("[cool time]"), ref unreadable);
        var cooltimeGroup = FirstInt32(
            ref reader, archive.GetTag("[cooltime group]"), ref unreadable);
        var minimumLevel = FirstInt32(
            ref reader, archive.GetTag("[minimum level]"), ref unreadable);
        var expBonusRate = FirstSingle(ref reader, archive.GetTag("[exp bonus rate]"), ref unreadable);
        var effectDuration = FirstInt32(
            ref reader, archive.GetTag("[stat change duration]"), ref unreadable);
        if (expBonusRate > 0 && effectDuration <= 0)
            unreadable = true;
        var jobText = AllStrings(archive, ref reader, archive.GetTag("[usable job]"));
        var placeText = AllStrings(archive, ref reader, archive.GetTag("[action usable place]"));

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
            EffectDurationMilliseconds: Math.Max(0, effectDuration));
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
    internal static int FirstInt32(ref PvfTokenReader reader, TagRef tag, ref bool unreadable)
    {
        reader.Reset();
        if (!tag.IsResolved || !reader.TryMovePast(tag))
            return 0;

        if (reader.TryReadInt32(out var value))
            return value;

        unreadable = true;
        return 0;
    }

    internal static double FirstSingle(ref PvfTokenReader reader, TagRef tag, ref bool unreadable)
    {
        reader.Reset();
        if (!tag.IsResolved || !reader.TryMovePast(tag))
            return 0;

        while (reader.TryReadSectionValue(out var token))
        {
            if (token.Kind != PvfTokenKind.Single)
                continue;
            var value = token.AsSingle();
            if (float.IsFinite(value) && value > 0)
                return value;
            break;
        }

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
        PvfArchive archive, ref PvfTokenReader reader, TagRef tag)
    {
        reader.Reset();
        if (!tag.IsResolved || !reader.TryMovePast(tag))
            return null;

        var values = new List<string>();
        while (reader.TryReadSectionValue(out var token))
        {
            if (token.IsString)
                values.Add(archive.ResolveString(token.Value));
        }

        return values;
    }
}
