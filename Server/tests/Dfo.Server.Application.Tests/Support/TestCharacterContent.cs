using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

// Character projection is incidental to lifecycle and inventory delivery tests.
internal sealed class TestCharacterContent : ICharacterStatDefinitionSource, ISkillRuleDefinitionSource
{
    public ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken token) =>
        ValueTask.FromResult(new CharacterStatValues(HpMax: 100).ToFixedPoint());

    public ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken token) =>
        ValueTask.FromResult<IReadOnlyList<CharacterStatDefinition>>(Enumerable.Range(0, 14)
            .Select(job => new CharacterStatDefinition((byte)job, new(HpMax: 100),
                new Dictionary<CharacterCareer, CharacterStatValues> { [new((byte)job)] = new(HpMax: 1) })).ToArray());

    public SkillRuleCatalog Get(byte job) => new(new Dictionary<int, SkillRuleDefinition>(), [],
        [(1, 100)], new HashSet<CharacterCareer> { new(job) });
}
