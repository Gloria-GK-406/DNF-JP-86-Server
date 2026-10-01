using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Inventory;

internal sealed class EffectCharacterStore(OwnedCharacterId owner) : ICharacterDomainStore, ICharacterDomainStoreSession
{
    public object StoreIdentity => this;
    public bool IsDurable => false;
    public ICharacterDomainStoreSession SessionIn(IDomainTransaction transaction) => this;
    public ValueTask<bool> HoldsAsync(OwnedCharacterId value, CancellationToken ct) => ValueTask.FromResult(value == owner);
    public IDomainStoreSession<OwnedCharacterId, long, StoredCreature> Creatures => throw new NotSupportedException();
    public ValueTask<CharacterPositionState?> ReadPositionAsync(OwnedCharacterId value, CancellationToken ct) => throw new NotSupportedException();
    public void WritePosition(OwnedCharacterId value, CharacterPositionState position) => throw new NotSupportedException();
}
