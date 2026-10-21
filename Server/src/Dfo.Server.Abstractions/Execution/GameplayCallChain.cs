using System.Collections.Immutable;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Execution;

/// <summary>Where a CMD came from (spec Definitions, "调用链").</summary>
public enum GameplayCallSource
{
    Connection,
    Timer,
    Event,
    Lifecycle,
    Gm,
    Http,
    StartupRecovery,
}

public static class GameplayCallDepth
{
    /// <summary>
    /// Spec appendix A: the longest legal chain is L = 10, so RQ-052 asks for at least
    /// max(2 × 10, 8) = 20; 32 leaves room for new subscriptions and still cuts a runaway chain.
    /// </summary>
    public const int Limit = 32;
}

public readonly record struct GameplayCallHop(GameplayCallSource Source, string CommandType);

/// <summary>
/// One CMD's place in its chain. A child keeps its ancestors' (source, CMD type) hops, capped to the
/// most recent <see cref="GameplayCallDepth.Limit"/> + 1, so a depth refusal can log the whole chain
/// even though each node only names its parent.
/// </summary>
public readonly struct GameplayCallChain
{
    private const string UnknownCommand = "(unknown)";
    private readonly ImmutableArray<GameplayCallHop> ancestors;

    private GameplayCallChain(
        Guid id,
        Guid rootId,
        Guid? parentId,
        int depth,
        GameplayCallSource source,
        NetworkConnectionId? sourceConnection,
        string? commandType,
        ImmutableArray<GameplayCallHop> ancestors)
    {
        Id = id;
        RootId = rootId;
        ParentId = parentId;
        Depth = depth;
        Source = source;
        SourceConnection = sourceConnection;
        CommandType = commandType;
        this.ancestors = ancestors;
    }

    public Guid Id { get; }
    public Guid RootId { get; }
    public Guid? ParentId { get; }
    public int Depth { get; }
    public GameplayCallSource Source { get; }

    /// <summary>The client connection the root came from, inherited by every descendant (RQ-053(a)).</summary>
    public NetworkConnectionId? SourceConnection { get; }

    /// <summary>The CMD this node is running as; set by the executor when the job starts.</summary>
    public string? CommandType { get; }

    public IReadOnlyList<GameplayCallHop> Ancestors => ancestors.IsDefault ? [] : ancestors;

    /// <summary>False only for <c>default</c>, which no producer may enqueue.</summary>
    public bool IsInitialized => RootId != Guid.Empty;

    public static GameplayCallChain NewRoot(GameplayCallSource source, NetworkConnectionId? connection = null)
    {
        var id = Guid.NewGuid();
        return new GameplayCallChain(id, id, null, 0, source, connection, null, []);
    }

    public GameplayCallChain Child(GameplayCallSource source)
    {
        var lineage = ancestors.IsDefault ? ImmutableArray<GameplayCallHop>.Empty : ancestors;
        if (lineage.Length > GameplayCallDepth.Limit) lineage = lineage.RemoveAt(0);
        return new GameplayCallChain(
            Guid.NewGuid(),
            RootId,
            Id,
            Depth + 1,
            source,
            SourceConnection,
            null,
            lineage.Add(new GameplayCallHop(Source, CommandType ?? UnknownCommand)));
    }

    public GameplayCallChain ForCommand(string commandType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        return new GameplayCallChain(Id, RootId, ParentId, Depth, Source, SourceConnection, commandType, ancestors);
    }

    /// <summary>Root to this node as "Source:Command" hops; a leading "..." marks dropped ancestors.</summary>
    public string Describe(string? commandType = null)
    {
        var hops = Ancestors
            .Select(hop => $"{hop.Source}:{hop.CommandType}")
            .Append($"{Source}:{commandType ?? CommandType ?? UnknownCommand}");
        var described = string.Join(" > ", hops);
        return Depth > Ancestors.Count ? "... > " + described : described;
    }

    public override string ToString() => Describe();
}
