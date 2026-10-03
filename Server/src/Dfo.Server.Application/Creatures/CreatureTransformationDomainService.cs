using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Creatures;

/// <summary>Resolves command targets. The egg and pet own the actual transitions.</summary>
internal sealed class CreatureTransformationDomainService(ICreatureDomainService creatures,
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions)
    : ICreatureTransformationDomainService
{
    public async ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session,
        BusinessSlot eggSlot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (transactions.Current is null || sessions.FindByCharacter(session.CharacterId) != session
            || eggSlot.Space != BusinessInventorySpace.CreatureInventory || eggSlot.Index is < 0 or > 139) return null;
        var container = creatures.GetStore(session.CharacterId).FindContainer(eggSlot.Space);
        var item = container is null ? null : await container.GetAsync(eggSlot.Index, ct).ConfigureAwait(false);
        if (item is null || item.Snapshot.LegacyKind != 5 || item.ItemUid is null) return null;
        var egg = await creatures.Of(session.CharacterId).FindEggAsync(item.ItemUid!.Value.Value, ct).ConfigureAwait(false);
        var pet = egg is null ? null : await egg.BirthAsync(ct).ConfigureAwait(false);
        return (pet as PetInstance)?.BirthReceipt;
    }

    public async ValueTask<CreatureTransformationReceipt?> EvolveAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (transactions.Current is null || sessions.FindByCharacter(session.CharacterId) != session) return null;
        var pet = await creatures.Of(session.CharacterId).CurrentPetAsync(ct).ConfigureAwait(false);
        return pet is null ? null : await pet.EvolveAsync(ct).ConfigureAwait(false);
    }
}
