using System.Collections.Immutable;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads only the linked block, retaining weights and uninterpreted entry conditions.</summary>
internal static class PvfLinkedDungeonParser
{
    internal static LinkedDungeonDefinition? Parse(IReadOnlyList<PvfSection> sections, string path)
    {
        var seen = false;
        var inside = false;
        string? subsection = null;
        var next = ImmutableArray.CreateBuilder<LinkedDungeonCandidate>();
        var previous = ImmutableArray.CreateBuilder<int>();
        LinkedDungeonClearObject? clear = null;
        int? noSelection = null;
        foreach (var section in sections)
        {
            if (section.Name == "[linked dungeon]")
            {
                Require(!seen && section.Values.Count == 0, "duplicate or invalid linked block");
                seen = inside = true;
                continue;
            }
            if (section.Name == "[/linked dungeon]")
            {
                Require(inside && subsection is null && section.Values.Count == 0, "unmatched linked block or subsection end");
                inside = false;
                continue;
            }
            if (!inside) continue;
            var tokens = section.Values;
            Require(tokens.All(value => value.Kind == PvfTokenKind.Int32), "expected Int32 values");
            var values = tokens.Select(value => value.Value).ToArray();
            switch (section.Name)
            {
                case "[next]":
                    Require(subsection is null, "nested linked subsection");
                    subsection = "next";
                    Require(values.Length > 0 && values.Length % 3 == 0, "truncated next dungeon/weight/condition triple");
                    for (var i = 0; i < values.Length; i += 3)
                    {
                        Require(values[i + 1] >= 0, "negative continuation weight");
                        if (values[i] == -1)
                        {
                            Require(noSelection is null && values[i + 2] == -1, "invalid no-selection entry");
                            noSelection = values[i + 1];
                        }
                        else
                        {
                            Require(values[i] > 0 && !next.Any(entry => entry.DungeonId == values[i]), "invalid or duplicate next dungeon");
                            next.Add(new(values[i], values[i + 1], values[i + 2]));
                        }
                    }
                    break;
                case "[prev]":
                    Require(subsection is null, "nested linked subsection");
                    subsection = "prev";
                    Require(values.Length > 0 && values.All(value => value > 0), "invalid predecessor");
                    foreach (var id in values)
                    {
                        Require(!previous.Contains(id), "duplicate predecessor");
                        previous.Add(id);
                    }
                    break;
                case "[on clear add passive object]":
                    Require(subsection is null, "clear object declared inside a linked subsection");
                    Require(clear is null && values.Length == 3 && values[0] > 0, "invalid clear object");
                    clear = new(values[0], values[1], values[2]);
                    break;
                case "[/next]":
                case "[/prev]":
                    Require(values.Length == 0 && section.Name == $"[/{subsection}]", "invalid subsection end");
                    subsection = null;
                    break;
                default:
                    throw new InvalidDataException($"PVF '{path}', '[linked dungeon]': unsupported section '{section.Name}'.");
            }
        }
        Require(!inside, "unterminated linked block");
        Require(next.Sum(value => (long)value.Weight) + (noSelection ?? 0) <= int.MaxValue, "continuation weights overflow");
        return seen ? new(next.ToImmutable(), noSelection ?? 0, previous.ToImmutable(), clear) : null;

        void Require(bool valid, string reason)
        {
            if (!valid) throw new InvalidDataException($"PVF '{path}', '[linked dungeon]': {reason}.");
        }
    }
}
