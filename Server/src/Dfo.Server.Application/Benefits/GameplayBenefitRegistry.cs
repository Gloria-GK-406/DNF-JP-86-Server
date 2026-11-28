using System.Collections.Immutable;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Execution;

namespace Dfo.Server.Application.Benefits;

internal sealed class GameplayBenefitRegistry : IGameplayBenefitRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<GameplayBenefitScope, Dictionary<string, (long Revision, GameplayBenefitBatch Batch)>> declarations = [];

    public bool Replace(GameplayBenefitBatch declaration, long revision)
    {
        GameplayWriteAssertion.Writing(nameof(GameplayBenefitRegistry) + "." + nameof(Replace));
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        lock (gate)
        {
            if (!declarations.TryGetValue(declaration.Scope, out var scoped))
            {
                scoped = new(StringComparer.Ordinal);
                declarations.Add(declaration.Scope, scoped);
            }
            if (scoped.TryGetValue(declaration.Source, out var current) && revision <= current.Revision)
                return false;
            scoped[declaration.Source] = (revision, declaration);
            return true;
        }
    }

    public ImmutableArray<GameplayBenefitBatch> Capture(IReadOnlyCollection<GameplayBenefitScope> scopes)
    {
        var captured = ImmutableArray.CreateBuilder<GameplayBenefitBatch>();
        lock (gate)
        {
            foreach (var scope in scopes.Distinct())
            {
                if (declarations.TryGetValue(scope, out var scoped))
                    captured.AddRange(scoped.Values.Select(entry => entry.Batch));
            }
        }
        return captured.ToImmutable();
    }
}

internal sealed class GameplayBenefitsQuery<TContext>(
    IGameplayBenefitRegistry registry, IEnumerable<IGameplayBenefitSource<TContext>> sources, TimeProvider clock)
{
    public ValueTask<GameplayBenefitSnapshot> ReadAsync(TContext context, CancellationToken ct,
        params GameplayBenefitScope[] scopes) => ReadAtAsync(context, clock.GetUtcNow(), ct, scopes);

    public async ValueTask<GameplayBenefitSnapshot> ReadAtAsync(TContext context, DateTimeOffset at, CancellationToken ct,
        params GameplayBenefitScope[] scopes)
    {
        ct.ThrowIfCancellationRequested();
        var declarations = registry.Capture(scopes).ToBuilder();
        foreach (var source in sources)
        {
            var declaration = await source.DescribeAsync(context, at, ct).ConfigureAwait(false);
            if (!scopes.Contains(declaration.Scope))
                throw new InvalidOperationException($"Benefit source {declaration.Source} declared an unrequested scope.");
            declarations.Add(declaration);
        }
        return new(at, declarations.ToImmutable());
    }
}
