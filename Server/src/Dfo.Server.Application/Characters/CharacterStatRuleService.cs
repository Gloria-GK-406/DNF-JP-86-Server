using System.Collections.Frozen;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterStatRuleService(ICharacterStatDefinitionSource source) : ICharacterStatRuleService
{
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private Prepared? _prepared;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _prepared) is not null) return;
        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_prepared is not null) return;
            var definitions = await source.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var table = new Dictionary<CharacterCareer, CharacterBaseStats[]>();
            foreach (var definition in definitions)
            {
                if (!definition.Growth.TryGetValue(new CharacterCareer(definition.Job), out var unadvanced))
                    throw new InvalidOperationException($"Job {definition.Job} has no unadvanced growth.");
                foreach (var (career, finalGrowth) in definition.Growth)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (career.Job != definition.Job
                        || !definition.Growth.TryGetValue(new CharacterCareer(career.Job, career.GrowType), out var advanced))
                        throw new InvalidOperationException($"Incomplete growth declaration for {career}.");
                    var levels = new CharacterBaseStats[86];
                    var accumulated = definition.Initial;
                    levels[0] = accumulated.ToFixedPoint();
                    for (var previousLevel = 1; previousLevel < levels.Length; previousLevel++)
                    {
                        accumulated += previousLevel <= 14 ? unadvanced
                            : previousLevel <= 49 ? advanced : finalGrowth;
                        levels[previousLevel] = accumulated.ToFixedPoint();
                    }
                    table.Add(career, levels);
                }
            }
            var careers = Array.AsReadOnly(table.Keys.OrderBy(c => c.Job)
                .ThenBy(c => c.GrowType).ThenBy(c => c.Awakening).ToArray());
            Volatile.Write(ref _prepared, new(table.ToFrozenDictionary(), careers));
        }
        finally { _initialization.Release(); }
    }

    public IReadOnlyList<CharacterCareer> SupportedCareers => Ready.Careers;

    public CharacterBaseStats GetStats(CharacterCareer career, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 86);
        return Ready.Table.TryGetValue(career, out var levels) ? levels[level - 1]
            : throw new InvalidOperationException($"The archive does not support career {career}.");
    }

    private Prepared Ready => Volatile.Read(ref _prepared)
        ?? throw new InvalidOperationException("Character stat rules have not been initialized.");

    private sealed record Prepared(
        FrozenDictionary<CharacterCareer, CharacterBaseStats[]> Table,
        IReadOnlyList<CharacterCareer> Careers);
}
