using Dfo.Pvf.Tokens;

namespace PvfPackageContentRepair;

/// <summary>One removed reward, or one container that the removal emptied.</summary>
public sealed record Change(string Container, int ItemId, int Count, string Reason);

/// <summary>A selection block's coordinate and how many rewards it offered before and after the repair.</summary>
public sealed record SelectionBlock(int C1, int C2, int Before, int After);

/// <summary>
/// Removes reward tuples from the reward containers of one package script, following the A21 client's
/// reader (<c>ImportStackableScript</c> 0x02999F40, see Document/tasks/pvf-package-content-340/README.md):
/// <list type="bullet">
/// <item><c>[package data]</c>, <c>[package data selection]</c>, <c>[package data include usable period]</c>: flat (id, count[, period]) lists.</item>
/// <item><c>[booster info]</c>: categories <c>[x] pick (id weight count [ext…])… [/x]</c>; <c>[charactor]</c> has a <c>`job` A B pick</c> header.</item>
/// <item><c>[booster select category] c1 c2</c>: modifiers plus categories of (id count [ext…]) tuples; closing tags are not required.</item>
/// </list>
/// Only containers that this repair empties are removed; containers that were already empty stay as they are.
/// A selection block always keeps its <c>c1 c2</c> header and closing tag, so the client's category keys never move.
/// Anything that does not parse the way the client reads it is copied verbatim and reported as an anomaly.
/// </summary>
public sealed class PackageScript
{
    private static readonly string[] FlatContainers = ["[package data]", "[package data selection]", "[package data include usable period]"];
    private static readonly string[] SingleValueModifiers = ["[booster equipment upgrade]", "[booster equipment separate]", "[booster equipment grade]"];
    private static readonly string[] PairListModifiers = ["[random probability]", "[force insert avatar option]"];
    private static readonly string[] IdListModifiers = ["[default select]", "[recommend]"];

    private readonly IReadOnlyList<Sym> _in;
    private readonly Func<int, string?> _removeReason;
    private readonly Func<int, int, string?>? _abilityReason;
    private readonly List<Sym> _out = [];

    public List<Change> Changes { get; } = [];
    public List<string> Anomalies { get; } = [];
    public List<SelectionBlock> Blocks { get; } = [];
    /// <summary>Every reward id the containers name, in order, before removal.</summary>
    public List<int> RewardIds { get; } = [];
    public int RewardsBefore { get; private set; }
    public int RewardsAfter { get; private set; }
    public IReadOnlyList<Sym> Result => _out;

    private PackageScript(IReadOnlyList<Sym> tokens, Func<int, string?> removeReason, Func<int, int, string?>? abilityReason)
    {
        _in = tokens;
        _removeReason = removeReason;
        _abilityReason = abilityReason;
    }

    /// <param name="abilityReason">
    /// For <c>[booster info]</c> avatar rows: (item id, ability index) → why the row goes, or null to keep it. A random pool
    /// grants exactly the row's ability (70: ext2 goes straight into the item); a selection's ability comes from the request.
    /// </param>
    public static PackageScript Repair(IReadOnlyList<Sym> tokens, Func<int, string?> removeReason, Func<int, int, string?>? abilityReason = null)
    {
        var script = new PackageScript(tokens, removeReason, abilityReason);
        script.Walk();
        return script;
    }

    public static bool IsTag(Sym s) => s.Kind == PvfTokenKind.Identifier && s.Text.StartsWith('[');
    private static bool IsTag(Sym s, string text) => IsTag(s) && s.Text == text;
    private static bool IsInt(Sym s) => s.Kind == PvfTokenKind.Int32;
    private static string CloseOf(string tag) => "[/" + tag[1..];

    private void Walk()
    {
        var i = 0;
        while (i < _in.Count)
        {
            var t = _in[i];
            if (!IsTag(t)) { _out.Add(t); i++; continue; }
            if (FlatContainers.Contains(t.Text)) i = Flat(i);
            else if (t.Text == "[booster info]") i = BoosterInfo(i);
            else if (t.Text == "[booster select category]") i = SelectBlock(i);
            else { _out.Add(t); i++; }
        }
    }

    private int IntRunEnd(int from, int limit)
    {
        var j = from;
        while (j < limit && IsInt(_in[j])) j++;
        return j;
    }

    private (int, int, int, int) Mark() => (Changes.Count, RewardIds.Count, RewardsBefore, RewardsAfter);

    private void Rollback((int Changes, int Ids, int Before, int After) mark)
    {
        Changes.RemoveRange(mark.Changes, Changes.Count - mark.Changes);
        RewardIds.RemoveRange(mark.Ids, RewardIds.Count - mark.Ids);
        RewardsBefore = mark.Before; RewardsAfter = mark.After;
    }

    private void Copy(int from, int to) { for (var k = from; k < to; k++) _out.Add(_in[k]); }

    /// <summary>
    /// Filters (id, …) tuples of <paramref name="stride"/> ints; returns the kept tokens.
    /// <paramref name="countAt"/> is the count's offset in a tuple; <paramref name="abilityAt"/> the avatar ability's, or -1.
    /// </summary>
    private List<Sym> Filter(int from, int to, int stride, string container, out int before, out int after, int countAt = 1, int abilityAt = -1)
    {
        var kept = new List<Sym>();
        before = 0; after = 0;
        for (var k = from; k < to; k += stride)
        {
            var id = _in[k].Value;
            before++;
            RewardIds.Add(id);
            var reason = _removeReason(id) ?? (abilityAt >= 0 ? _abilityReason?.Invoke(id, _in[k + abilityAt].Value) : null);
            if (reason is null) { after++; for (var s = 0; s < stride; s++) kept.Add(_in[k + s]); }
            else Changes.Add(new(container, id, _in[k + countAt].Value, reason));
        }
        RewardsBefore += before;
        RewardsAfter += after;
        return kept;
    }

    private int Flat(int i)
    {
        var tag = _in[i].Text;
        var stride = tag == "[package data include usable period]" ? 3 : 2;
        var end = IntRunEnd(i + 1, _in.Count);
        var hasClose = end < _in.Count && IsTag(_in[end], CloseOf(tag));
        var next = hasClose ? end + 1 : end;
        if ((end - i - 1) % stride != 0)
        {
            Anomalies.Add($"{tag}: {end - i - 1} ints, not a multiple of {stride}");
            Copy(i, next);
            return next;
        }
        var kept = Filter(i + 1, end, stride, tag, out var before, out var after);
        if (after == 0 && before > 0)
        {
            Changes.Add(new(tag, 0, 0, "container emptied: tag removed"));
            return next;
        }
        _out.Add(_in[i]);
        _out.AddRange(kept);
        if (hasClose) _out.Add(_in[end]);
        return next;
    }

    private int BoosterInfo(int i)
    {
        var close = -1;
        for (var k = i + 1; k < _in.Count; k++) if (IsTag(_in[k], "[/booster info]")) { close = k; break; }
        if (close < 0) { Anomalies.Add("[booster info] without [/booster info]"); _out.Add(_in[i]); return i + 1; }

        var categories = new List<List<Sym>>();
        var emptied = 0;
        var p = i + 1;
        var mark = Mark();
        while (p < close)
        {
            var open = _in[p];
            if (!IsTag(open) || open.Text.StartsWith("[/", StringComparison.Ordinal)) return BoosterInfoVerbatim(mark, i, close, $"[booster info]: unexpected {open} at {p}");
            var name = open.Text;
            var header = name == "[charactor]" ? 4 : 1;
            var q = p + 1 + header;
            if (q > close) return BoosterInfoVerbatim(mark, i, close, $"[booster info] {name}: truncated header");
            if (name == "[charactor]" ? !(_in[p + 1].IsString && IsInt(_in[p + 2]) && IsInt(_in[p + 3]) && IsInt(_in[p + 4])) : !IsInt(_in[p + 1]))
                return BoosterInfoVerbatim(mark, i, close, $"[booster info] {name}: malformed header");
            var r = IntRunEnd(q, close);
            if (r >= close || !IsTag(_in[r], CloseOf(name))) return BoosterInfoVerbatim(mark, i, close, $"[booster info] {name}: closing tag missing");
            var stride = name is "[avatar]" or "[special avatar]" ? 5 : name == "[broadcast]" ? 4 : 3;
            if ((r - q) % stride != 0) return BoosterInfoVerbatim(mark, i, close, $"[booster info] {name}: {r - q} ints, not a multiple of {stride}");
            var kept = Filter(q, r, stride, "[booster info] " + name, out var before, out var after, countAt: 2, abilityAt: stride == 5 ? 4 : -1);
            if (after == 0 && before > 0)
            {
                emptied++;
                Changes.Add(new("[booster info] " + name, 0, 0, "category emptied: category removed"));
            }
            else
            {
                var cat = new List<Sym>();
                for (var k = p; k < q; k++) cat.Add(_in[k]);
                cat.AddRange(kept);
                cat.Add(_in[r]);
                categories.Add(cat);
            }
            p = r + 1;
        }
        if (categories.Count == 0 && emptied > 0)
        {
            Changes.Add(new("[booster info]", 0, 0, "every category emptied: [booster info] removed"));
            return close + 1;
        }
        _out.Add(_in[i]);
        foreach (var cat in categories) _out.AddRange(cat);
        _out.Add(_in[close]);
        return close + 1;
    }

    private int BoosterInfoVerbatim((int, int, int, int) mark, int i, int close, string why)
    {
        Rollback(mark);
        Anomalies.Add(why);
        Copy(i, close + 1);
        return close + 1;
    }

    private int SelectBlock(int i)
    {
        if (i + 2 >= _in.Count || !IsInt(_in[i + 1]) || !IsInt(_in[i + 2]))
        {
            Anomalies.Add("[booster select category] without two coordinates");
            _out.Add(_in[i]);
            return i + 1;
        }
        var c1 = _in[i + 1].Value; var c2 = _in[i + 2].Value;
        var close = -1;
        for (var k = i + 3; k < _in.Count; k++)
        {
            if (IsTag(_in[k], "[/booster select category]")) { close = k; break; }
            if (IsTag(_in[k], "[booster select category]")) break;
        }
        if (close < 0)
        {
            Anomalies.Add($"[booster select category] {c1} {c2}: closing tag missing");
            Copy(i, i + 3);
            return i + 3;
        }

        var mark = Mark();
        var body = new List<Sym>();
        var before = 0; var after = 0;
        var p = i + 3;
        while (p < close)
        {
            var t = _in[p];
            if (!IsTag(t)) return SelectVerbatim(mark, i, close, c1, c2, $"[booster select category] {c1} {c2}: unexpected {t} at {p}");
            if (SingleValueModifiers.Contains(t.Text))
            {
                if (p + 1 >= close || !IsInt(_in[p + 1])) return SelectVerbatim(mark, i, close, c1, c2, $"{t.Text} without a value");
                body.Add(t); body.Add(_in[p + 1]); p += 2; continue;
            }
            if (PairListModifiers.Contains(t.Text))
            {
                var e = IntRunEnd(p + 1, close);
                for (var k = p; k < e; k++) body.Add(_in[k]);
                p = e; continue;
            }
            if (IdListModifiers.Contains(t.Text))
            {
                if (p + 1 >= close || !IsInt(_in[p + 1])) return SelectVerbatim(mark, i, close, c1, c2, $"{t.Text} without a count");
                var n = _in[p + 1].Value;
                if (n < 0 || p + 2 + n > close || Enumerable.Range(p + 2, n).Any(k => !IsInt(_in[k])))
                    return SelectVerbatim(mark, i, close, c1, c2, $"{t.Text} declares {n} ids that are not there");
                var ids = Enumerable.Range(p + 2, n).Select(k => _in[k]).ToList();
                var keptIds = ids.Where(s => _removeReason(s.Value) is null).ToList();
                foreach (var gone in ids.Where(s => _removeReason(s.Value) is not null))
                    Changes.Add(new($"[booster select category] {c1} {c2} {t.Text}", gone.Value, 0, "marker of a removed reward: " + _removeReason(gone.Value)));
                if (keptIds.Count > 0)
                {
                    body.Add(t); body.Add(Sym.Int(keptIds.Count)); body.AddRange(keptIds);
                }
                else if (n > 0) Changes.Add(new($"[booster select category] {c1} {c2} {t.Text}", 0, 0, "every marked id removed: tag removed"));
                else { body.Add(t); body.Add(_in[p + 1]); }
                p += 2 + n; continue;
            }
            if (t.Text.StartsWith("[/", StringComparison.Ordinal)) { body.Add(t); p++; continue; }
            // A category: the client stops at the first non-integer and does not require the closing tag.
            var name = t.Text;
            var stride = name is "[avatar]" or "[special avatar]" ? 4 : name == "[broadcast]" ? 3 : 2;
            var r = IntRunEnd(p + 1, close);
            if ((r - p - 1) % stride != 0) return SelectVerbatim(mark, i, close, c1, c2, $"[booster select category] {c1} {c2} {name}: {r - p - 1} ints, not a multiple of {stride}");
            var kept = Filter(p + 1, r, stride, $"[booster select category] {c1} {c2} {name}", out var b, out var a);
            before += b; after += a;
            if (a == 0 && b > 0)
            {
                Changes.Add(new($"[booster select category] {c1} {c2} {name}", 0, 0, "category emptied: category removed"));
                p = r < close && IsTag(_in[r], CloseOf(name)) ? r + 1 : r;
                continue;
            }
            body.Add(t);
            body.AddRange(kept);
            p = r;
        }
        Blocks.Add(new(c1, c2, before, after));
        _out.Add(_in[i]); _out.Add(_in[i + 1]); _out.Add(_in[i + 2]);
        _out.AddRange(body);
        _out.Add(_in[close]);
        return close + 1;
    }

    private int SelectVerbatim((int, int, int, int) mark, int i, int close, int c1, int c2, string why)
    {
        Rollback(mark);
        Anomalies.Add(why);
        Copy(i, close + 1);
        Blocks.Add(new(c1, c2, -1, -1));
        return close + 1;
    }
}
