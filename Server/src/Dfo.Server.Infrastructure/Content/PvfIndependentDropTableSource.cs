using System.Collections.Immutable;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal readonly record struct IndependentDropToken(int? Number, string? Tag);

internal sealed class PvfIndependentDropTableSource(PvfArchiveProvider provider) : IIndependentDropTableSource
{
    internal const string Path = "etc/independent_drop.etc";

    public IndependentDropTable Load()
    {
        var archive = provider.Archive;
        var index = archive.FindFile(Path);
        if (index < 0) throw new InvalidDataException($"Missing {Path}.");
        var reader = archive.OpenScript(index);
        var tokens = new List<IndependentDropToken>();
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32) tokens.Add(new(token.Value, null));
            else if (token.Kind == PvfTokenKind.Identifier) tokens.Add(new(null, archive.ResolveString(token.Value)));
            else throw new InvalidDataException($"{Path}: unexpected token {token.Kind}.");
        }
        var rows = Parse(tokens);
        var metadata = new PvfDropItemReader(archive);
        var items = ImmutableDictionary.CreateBuilder<int, RolledDrop?>();
        foreach (var id in rows.SelectMany(row => row.PoolMode == 0
                     ? new[] { row.ItemId } : row.Choices.Select(choice => choice.ItemId)).Distinct())
            items.Add(id, metadata.Get(id));
        return new(rows, items.ToImmutable());
    }

    internal static ImmutableArray<IndependentDropRow> Parse(IReadOnlyList<IndependentDropToken> tokens)
    {
        var index = 0;
        int Read()
        {
            if (index >= tokens.Count || tokens[index].Number is not { } value)
                throw new InvalidDataException($"{Path}: expected integer at token {index}.");
            index++;
            return value;
        }
        void Tag(string expected)
        {
            if (index >= tokens.Count || tokens[index].Tag != expected)
                throw new InvalidDataException($"{Path}: expected {expected} at token {index}.");
            index++;
        }
        Tag("[independent drop]");
        var rows = ImmutableArray.CreateBuilder<IndependentDropRow>();
        while (index < tokens.Count && tokens[index].Tag != "[/independent drop]")
        {
            var fields = ImmutableArray.CreateBuilder<int>(17);
            for (var column = 0; column < 17; column++) fields.Add(Read());
            if (fields[0] is < 0 or > 1 || fields[1] <= 0 || fields[2] < 0
                || (fields[13] > 0 && fields[14] > 0 && fields[13] > fields[14])
                || fields.Skip(3).Take(12).Any(value => value < 0) || fields[15] < -1)
                throw new InvalidDataException($"{Path}: invalid fields in row {rows.Count}.");
            var choices = ImmutableArray.CreateBuilder<IndependentDropChoice>();
            if (fields[16] == 1)
            {
                Tag("[list]");
                while (index < tokens.Count && tokens[index].Tag != "[/list]")
                {
                    var id = Read();
                    var weight = Read();
                    if (id <= 0 || weight < 0)
                        throw new InvalidDataException($"{Path}: invalid pool entry in row {rows.Count}.");
                    choices.Add(new(id, weight));
                }
                Tag("[/list]");
                if (choices.Count == 0) throw new InvalidDataException($"{Path}: empty list in row {rows.Count}.");
            }
            else if (fields[16] != 0 || fields[2] <= 0)
                throw new InvalidDataException($"{Path}: unsupported pool mode or missing direct item in row {rows.Count}.");
            rows.Add(new(rows.Count, fields.MoveToImmutable(), choices.ToImmutable()));
        }
        Tag("[/independent drop]");
        if (index != tokens.Count || rows.Count == 0)
            throw new InvalidDataException($"{Path}: trailing tokens or no rows.");
        return rows.ToImmutable();
    }
}
