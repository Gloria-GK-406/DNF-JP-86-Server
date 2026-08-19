using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>What the archive would say about a handful of items.</summary>
/// <remarks>
/// An identifier absent from the table is an identifier the archive lists nothing usable under --
/// which is how equipment reaches the use path and how it is turned away.
/// </remarks>
internal sealed class FakeItemUseRules(params (int ItemId, ItemUseRule Rule)[] rules)
    : IItemUseRuleDefinitionSource
{
    private readonly Dictionary<int, ItemUseRule> declared =
        rules.ToDictionary(entry => entry.ItemId, entry => entry.Rule);

    public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(declared.GetValueOrDefault(itemId));
}

/// <summary>The shapes the archive actually produces, named once.</summary>
internal static class ItemUseRules
{
    /// <summary>A potion: consumed, ten seconds, no shared group, no restriction.</summary>
    public static ItemUseRule Potion(int cooldownMilliseconds = 10_000, int cooltimeGroup = 0) =>
        new(
            IsConsumedOnUse: true,
            CooldownMilliseconds: cooldownMilliseconds,
            CooltimeGroup: cooltimeGroup,
            MinimumLevel: 1,
            Jobs: ItemJobRestriction.Any,
            Places: ItemPlaceRestriction.Anywhere);

    /// <summary>One of the eighty-four whose <c>[stackable type]</c> begins with unlimited.</summary>
    public static ItemUseRule Unlimited(int cooldownMilliseconds = 0, int cooltimeGroup = 0) =>
        Potion(cooldownMilliseconds, cooltimeGroup) with { IsConsumedOnUse = false };

    public static ItemUseRule RequiringLevel(int minimumLevel) =>
        Potion(cooldownMilliseconds: 0) with { MinimumLevel = minimumLevel };

    public static ItemUseRule RequiringJob(params QuestJobRequirement[] families) =>
        Potion(cooldownMilliseconds: 0) with
        {
            Jobs = new ItemJobRestriction(IsExactAll: false, families),
        };

    public static ItemUseRule UsableIn(params ItemUsePlace[] places) =>
        Potion(cooldownMilliseconds: 0) with
        {
            Places = new ItemPlaceRestriction(IsUnrestricted: false, places),
        };
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class AdvanceableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan amount) => now += amount;
}

/// <summary>
/// One character, published, with the level and job the test cares about.
/// </summary>
/// <remarks>
/// Every other member of the entry result is left null. The usability service reads
/// <see cref="SelectCharacterResult.Character"/> and nothing else, and a fixture that filled in
/// the other eight would hide the day it starts reading a ninth.
/// </remarks>
internal sealed class FixedRuntimeSessions(byte level, byte job, bool published = true)
    : ISelectedSessionRuntimeDirectory
{
    /// <summary>A directory that has not been published to yet, as during entry.</summary>
    public static FixedRuntimeSessions Unpublished => new(level: 0, job: 0, published: false);

    private readonly SelectCharacterResult result = new(
        new ActiveCharacterState(
            new CharacterIdentityState(new CharacterId(60), 0, [], job, GrowType: 0),
            new CharacterProgressionState(level, 0),
            new CharacterFatigueState(0, 0),
            new CharacterPositionState(1, 0, 0, 0, 0),
            DateTimeOffset.UnixEpoch,
            DarkKnightCombo: null),
        null!, null!, null!, null!, null!, null!);

    public void Publish(
        SelectedSessionIdentity session, SelectCharacterResult value, bool shouldPersistPosition) =>
        throw new NotSupportedException("This fixture only answers.");

    public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult value) =>
        throw new NotSupportedException("This fixture only answers.");

    public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
        published ? result : throw new KeyNotFoundException("Nothing was published for it.");

    public bool TryGet(
        SelectedSessionIdentity session,
        out SelectCharacterResult? value,
        out bool shouldPersistPosition)
    {
        value = published ? result : null;
        shouldPersistPosition = false;
        return published;
    }

    public bool RemoveExact(SelectedSessionIdentity session) => false;
}

/// <summary>
/// Runs a command on the handler that owns it, without the pipeline in between.
/// </summary>
/// <remarks>
/// The pipeline's behaviours -- execution kind, user access scoping -- are the bus's own subject
/// and are covered where the bus is. What these tests need from it is only that the protocol
/// handler no longer decides anything the command decides.
/// </remarks>
internal sealed class DirectCommandBus(
    ICommandHandler<UseStackableCommand, ItemUseResult>? use = null,
    ICommandHandler<MoveItemSpaceCommand, ItemMoveResult>? move = null) : ICommandBus
{
    public async ValueTask<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        var context = new CommandExecutionContext(
            Guid.Empty, command.GetType(), typeof(DirectCommandBus), CommandExecutionKind.Direct);

        object dispatched = command switch
        {
            UseStackableCommand value => await use!
                .HandleAsync(context, value, cancellationToken).ConfigureAwait(false),
            MoveItemSpaceCommand value => await move!
                .HandleAsync(context, value, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"No handler registered for {command.GetType()}."),
        };

        return (TResult)dispatched;
    }
}
