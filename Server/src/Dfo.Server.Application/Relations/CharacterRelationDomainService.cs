using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Relations;

/// <summary>The one owner of character relations, behind both the read and the write face.</summary>
/// <remarks>
/// Passive: it keeps relations and answers what a change did, and never tells anyone. Its
/// dependencies are held to the store, the identity read, the relation rules, the transaction and
/// the clock by <c>RelationPassivityArchitectureTests</c>.
/// </remarks>
internal sealed class CharacterRelationDomainService(ICharacterRelationDomainStore store,
    ICharacterIdentityQueryDomainService identities, ICharacterRelationRuleService rules,
    IDomainTransactionAccessor transactions, TimeProvider clock)
    : ICharacterRelationDomainService, ICharacterRelationQueryDomainService
{

    public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(
        CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        store.ListAsync(owner, Known(kind), cancellationToken);

    public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(
        CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        store.OwnersOfAsync(target, Known(kind), cancellationToken);

    public ValueTask<bool> ExistsAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
        store.ExistsAsync(owner, target, Known(kind), cancellationToken);

    /// <remarks>
    /// The order is the outcome's precedence: a target already present is reported as present even
    /// on a full list, because for a friend that is the success the client expects. The owner is the
    /// caller's own character; one that is not live is a caller error, not a refusal to report.
    /// </remarks>
    public async ValueTask<CharacterRelationAddOutcome> AddAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken)
    {
        RequireWritable();
        var capacity = rules.Capacity(kind);
        if (owner == target) return CharacterRelationAddOutcome.Self;
        var live = await identities.ReadAsync([owner, target], cancellationToken).ConfigureAwait(false);
        if (!live.ContainsKey(owner))
            throw new InvalidOperationException($"Character {owner.Value} is not a live character and cannot hold relations.");
        if (!live.ContainsKey(target)) return CharacterRelationAddOutcome.TargetMissing;
        if (await store.ExistsAsync(owner, target, kind, cancellationToken).ConfigureAwait(false))
            return CharacterRelationAddOutcome.AlreadyPresent;
        if (await store.CountAsync(owner, kind, cancellationToken).ConfigureAwait(false) >= capacity)
            return CharacterRelationAddOutcome.Full;
        return await store.InsertAsync(owner, target, kind, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false)
            ? CharacterRelationAddOutcome.Added
            : CharacterRelationAddOutcome.AlreadyPresent;
    }

    public async ValueTask<CharacterRelationRemoveOutcome> RemoveAsync(
        CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken)
    {
        RequireWritable();
        return await store.DeleteAsync(owner, target, Known(kind), cancellationToken).ConfigureAwait(false)
            ? CharacterRelationRemoveOutcome.Removed
            : CharacterRelationRemoveOutcome.Missing;
    }

    public async ValueTask<CharacterRelationPurge> PurgeAsync(CharacterId character, CancellationToken cancellationToken)
    {
        RequireWritable();
        return new(await store.PurgeAsync(character, cancellationToken).ConfigureAwait(false));
    }

    private CharacterRelationKind Known(CharacterRelationKind kind)
    {
        _ = rules.Capacity(kind);
        return kind;
    }

    private void RequireWritable()
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Changing character relations requires a writable transaction.");
    }
}
