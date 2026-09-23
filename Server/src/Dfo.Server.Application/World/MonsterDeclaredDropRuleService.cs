using System.Collections.Frozen;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Application.World;

internal sealed class MonsterDeclaredDropRuleService(IMonsterDeclaredDropTableSource source) : IMonsterDeclaredDropRuleService
{
    private readonly Lock gate = new();
    private FrozenDictionary<int, MonsterDeclaredDropTable>? tables;
    public int Initialize()
    {
        lock (gate)
        {
            if (tables is not null) return tables.Count;
            var loaded = source.Load();
            foreach (var table in loaded)
                if (table.Entries.Any(entry => entry.Weight < 0 || entry.Item.ItemId <= 0)
                    || table.Entries.Sum(entry => (long)entry.Weight) > int.MaxValue)
                    throw new InvalidDataException($"Monster {table.MonsterId}: invalid declared drop distribution.");
            Volatile.Write(ref tables, loaded.ToFrozenDictionary(table => table.MonsterId));
            return tables.Count;
        }
    }
    public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request)
    {
        var snapshot = Volatile.Read(ref tables) ?? throw new InvalidOperationException("Monster declared drops have not been initialized.");
        if (!snapshot.TryGetValue(request.MonsterCode, out var table)) return [];
        var context = request.Context;
        var random = new DropRandom(DropRandom.SeedFor(context.Seed, context.MapId,
            context.Cell.X, context.Cell.Y, request.ActorSequence) ^ 0x4D4F_4249_5445_4D53UL);
        var item = Select(table, random.Next(Basis(table)) - 1);
        return item is null ? [] : [item.Value];
    }
    internal static int Basis(MonsterDeclaredDropTable table) =>
        (int)Math.Max(10000L, table.Entries.Sum(entry => (long)entry.Weight));

    internal static RolledDrop? Select(MonsterDeclaredDropTable table, int ticket)
    {
        if (ticket < 0 || ticket >= Basis(table)) throw new ArgumentOutOfRangeException(nameof(ticket));
        var end = 0;
        foreach (var entry in table.Entries)
        {
            end += entry.Weight;
            if (ticket < end) return entry.Item with { Count = 1 };
        }
        return null;
    }
}
