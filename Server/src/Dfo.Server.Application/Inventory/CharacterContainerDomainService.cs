using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Character-local current bindings, with explicit default fallbacks.</summary>
internal sealed class CharacterContainerDomainService(IDomainTransactionAccessor? transactions = null) : ICharacterContainerDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<int, Held> byCharacter = [];

    private T Access<T>(CharacterId character, bool mutate, Func<Held, T> action)
    {
        lock (sync)
        {
            if (transactions?.Current is { } transaction)
                return transaction.Enlist(this, () => new Changes(this)).Access(character.Value, mutate, action);
            var held = byCharacter.GetValueOrDefault(character.Value) ?? new Held();
            var result = action(held);
            if (mutate)
            {
                held.Version++;
                if (held.Registered.Count == 0) byCharacter.Remove(character.Value);
                else byCharacter[character.Value] = held;
            }
            return result;
        }
    }
    public void Register(CharacterId characterId, IItemContainer container)
    {
        GameplayWriteAssertion.Writing(nameof(CharacterContainerDomainService) + "." + nameof(Register));
        ArgumentNullException.ThrowIfNull(container);
        Access(characterId, true, held =>
        {
            if (held.Registered.Any(x => x.Id == container.Id || container.IsDefault && x.IsDefault && x.Type == container.Type))
                throw new InvalidOperationException($"Duplicate container or default for {container.Type}.");
            if (container.IsDefault && container.IsRemovable)
                throw new InvalidOperationException("A default container cannot be removable.");
            held.Registered.Add(container);
            held.Current.TryAdd(container.Type, container.Id);
            return true;
        });
    }
    public void ReplaceDefault(CharacterId characterId, ContainerId expectedDefaultId, IItemContainer replacement)
    {
        GameplayWriteAssertion.Writing(nameof(CharacterContainerDomainService) + "." + nameof(ReplaceDefault));
        ArgumentNullException.ThrowIfNull(replacement);
        Access(characterId, true, held =>
        {
            var index = held.Registered.FindIndex(x => x.Id == expectedDefaultId && x.IsDefault);
            if (index < 0 || !replacement.IsDefault || replacement.IsRemovable
                || held.Registered[index].Type != replacement.Type
                || held.Registered.Where((_, i) => i != index).Any(x => x.Id == replacement.Id))
                throw new InvalidOperationException("Replacement must name the existing default and preserve its type and default role.");
            held.Registered[index] = replacement;
            if (held.Current.GetValueOrDefault(replacement.Type) == expectedDefaultId)
                held.Current[replacement.Type] = replacement.Id;
            return true;
        });
    }
    public bool Remove(CharacterId characterId, ContainerId containerId)
    {
        GameplayWriteAssertion.Writing(nameof(CharacterContainerDomainService) + "." + nameof(Remove));
        return Access(characterId, true, held =>
        {
            var container = held.Registered.SingleOrDefault(x => x.Id == containerId);
            if (container is null) return false;
            if (!container.IsRemovable || container.IsDefault)
                throw new InvalidOperationException($"Container {containerId} cannot be removed.");
            if (held.Current.GetValueOrDefault(container.Type) == containerId)
            {
                var fallback = held.Registered.SingleOrDefault(x => x.Type == container.Type && x.IsDefault)
                    ?? throw new InvalidOperationException($"Removing {containerId} would leave no default container.");
                held.Current[container.Type] = fallback.Id;
            }
            held.Registered.Remove(container);
            return true;
        });
    }
    public IReadOnlyList<IItemContainer> GetContainers(CharacterId characterId, InventoryContainerType type) =>
        Access<IReadOnlyList<IItemContainer>>(characterId, false, held => held.Registered.Where(x => x.Type == type).ToArray());
    public IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false) =>
        FindContainer(characterId, type, isDefault) ?? throw new InvalidOperationException($"Character {characterId.Value} has no {type} container.");
    public IItemContainer? FindContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false) =>
        Access(characterId, false, held => isDefault
            ? held.Registered.SingleOrDefault(x => x.Type == type && x.IsDefault)
            : held.Registered.SingleOrDefault(x => x.Id == held.Current.GetValueOrDefault(type)));
    public bool UseContainer(CharacterId characterId, ContainerId containerId)
    {
        GameplayWriteAssertion.Writing(nameof(CharacterContainerDomainService) + "." + nameof(UseContainer));
        return Access(characterId, true, held =>
        {
            var container = held.Registered.SingleOrDefault(x => x.Id == containerId);
            if (container is null) return false;
            held.Current[container.Type] = containerId;
            return true;
        });
    }
    public void Clear(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(CharacterContainerDomainService) + "." + nameof(Clear));
        Access(characterId, true, held =>
        {
            held.Registered.Clear(); held.Current.Clear(); return true;
        });
    }
    private sealed class Held
    {
        public long Version;
        public List<IItemContainer> Registered = [];
        public Dictionary<InventoryContainerType, ContainerId> Current = [];
        public Held Clone() => new() { Version = Version, Registered = [.. Registered], Current = new(Current) };
    }
    private sealed class Changes(CharacterContainerDomainService owner) : ITransactionParticipant, ITransactionFailureSource
    {
        private readonly Dictionary<int, (Held? Before, long Version, Held After)> staged = [];
        private readonly HashSet<int> dirty = [];
        public TransactionFailureFact? LastFailure { get; private set; }
        public bool CommitCanFail => false;
        public T Access<T>(int id, bool mutate, Func<Held, T> action)
        {
            if (!staged.TryGetValue(id, out var entry))
            {
                var before = owner.byCharacter.GetValueOrDefault(id);
                entry = (before, before?.Version ?? 0, before?.Clone() ?? new Held());
                staged.Add(id, entry);
            }
            var result = action(entry.After);
            if (mutate) dirty.Add(id);
            return result;
        }
        public ValueTask PrepareAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (owner.sync)
                foreach (var (id, entry) in staged)
                    if (owner.byCharacter.GetValueOrDefault(id) != entry.Before || entry.Before?.Version != (entry.Before is null ? null : entry.Version))
                    {
                        var cause = new InvalidOperationException("Container bindings changed before commit.");
                        LastFailure = new(this, cause, TransactionFailurePhase.Prepare, TransactionFailureKind.Guard);
                        throw cause;
                    }
            return ValueTask.CompletedTask;
        }
        public ValueTask CommitAsync(CancellationToken ct)
        {
            lock (owner.sync)
                foreach (var id in dirty)
                {
                    var next = staged[id].After;
                    next.Version++;
                    if (next.Registered.Count == 0) owner.byCharacter.Remove(id);
                    else owner.byCharacter[id] = next;
                }
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
