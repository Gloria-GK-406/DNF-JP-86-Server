using System.Collections.Immutable;
using System.Globalization;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Benefits;

public interface IGameplayBenefitEffect;

public readonly record struct GameplayBenefitScope(string Kind, string Key)
{
    public static GameplayBenefitScope Global { get; } = new("global", "");
    public static GameplayBenefitScope For(AccountId account) =>
        new("account", account.Value.ToString(CultureInfo.InvariantCulture));
    public static GameplayBenefitScope For(CharacterId character) =>
        new("character", character.Value.ToString(CultureInfo.InvariantCulture));
}

public sealed record GameplayBenefit(string Key, string Name, int Order, IGameplayBenefitEffect Effect,
    DateTimeOffset? StartsAt = null, DateTimeOffset? EndsAt = null)
{
    public bool IsActiveAt(DateTimeOffset at) =>
        (StartsAt is null || StartsAt <= at) && (EndsAt is null || at < EndsAt);
}

/// <summary>An authoritative declaration, including a declaration of no current benefits.</summary>
public sealed record GameplayBenefitBatch
{
    public GameplayBenefitBatch(string source, GameplayBenefitScope scope, params ImmutableArray<GameplayBenefit> benefits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Kind);
        ArgumentNullException.ThrowIfNull(scope.Key);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var benefit in benefits)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(benefit.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(benefit.Name);
            ArgumentNullException.ThrowIfNull(benefit.Effect);
            if (!keys.Add(benefit.Key) || benefit.StartsAt >= benefit.EndsAt)
                throw new ArgumentException($"Invalid benefit {source}/{benefit.Key}.", nameof(benefits));
        }
        Source = source;
        Scope = scope;
        Benefits = benefits;
    }

    public string Source { get; }
    public GameplayBenefitScope Scope { get; }
    public ImmutableArray<GameplayBenefit> Benefits { get; }
}

/// <summary>Read-through producers declare from state they already own.</summary>
public interface IGameplayBenefitSource<in TContext>
{
    ValueTask<GameplayBenefitBatch> DescribeAsync(TContext context, DateTimeOffset at, CancellationToken ct);
}

/// <summary>Versioned live publications. Publishers own the lifetime and recovery of their authoritative state.</summary>
public interface IGameplayBenefitRegistry
{
    /// <summary>A strictly newer revision replaces the entire source and scope, including an empty declaration.</summary>
    bool Replace(GameplayBenefitBatch declaration, long revision);
    ImmutableArray<GameplayBenefitBatch> Capture(IReadOnlyCollection<GameplayBenefitScope> scopes);
}

public sealed record GameplayBenefitEntry(string Source, GameplayBenefitScope Scope, GameplayBenefit Benefit);

public sealed class GameplayBenefitSnapshot
{
    public GameplayBenefitSnapshot(DateTimeOffset at, ImmutableArray<GameplayBenefitBatch> declarations)
    {
        At = at;
        Declarations = declarations;
        if (declarations.Select(batch => (batch.Source, batch.Scope)).Distinct().Count() != declarations.Length)
            throw new InvalidOperationException("Multiple publishers declared the same gameplay benefit source and scope.");
        Active = declarations.SelectMany(batch => batch.Benefits.Where(benefit => benefit.IsActiveAt(at))
                .Select(benefit => new GameplayBenefitEntry(batch.Source, batch.Scope, benefit)))
            .OrderBy(item => item.Benefit.Order)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Scope.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Scope.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Benefit.Key, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public DateTimeOffset At { get; }
    public ImmutableArray<GameplayBenefitBatch> Declarations { get; }
    public ImmutableArray<GameplayBenefitEntry> Active { get; }
    public IEnumerable<TEffect> Effects<TEffect>() where TEffect : IGameplayBenefitEffect =>
        Active.Select(entry => entry.Benefit.Effect).OfType<TEffect>();
}
