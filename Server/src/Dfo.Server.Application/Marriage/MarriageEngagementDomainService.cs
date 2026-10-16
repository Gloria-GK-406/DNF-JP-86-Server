using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageEngagementDomainService(IMarriageProposalDomainStore store,
    IMarriageCeremonyDomainStore ceremonies, ICharacterDomainService characters,
    ICharacterInventoryEntryDomainService inventory) : IMarriageEngagementDomainService
{
    public async ValueTask<MarriageEngagementSnapshot> ReadAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        var relation = await store.FindActiveAsync(character, ct).ConfigureAwait(false);
        if (relation is not { State: MarriageProposalState.Engaged }) return MarriageEngagementSnapshot.Empty;
        var own = relation.Proposer.CharacterId == character ? relation.Proposer : relation.Recipient;
        if (own.AccountId != account) return MarriageEngagementSnapshot.Empty;
        var peer = relation.Proposer.CharacterId == character ? relation.Recipient : relation.Proposer;
        var domain = characters.Get(peer.CharacterId);
        var identity = await domain.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await domain.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null) return MarriageEngagementSnapshot.Empty;
        var current = await inventory.ReadCurrentAsync(peer.AccountId, peer.CharacterId, ct).ConfigureAwait(false);
        var worn = current.Containers.SingleOrDefault(x => x.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        var equipment = Enumerable.Range(0, 13).Select(slot => worn.SingleOrDefault(x => x.SlotIndex == slot)?.Core.ItemId ?? 0).ToArray();
        return new(relation.Id, new(peer.CharacterId.Value, identity.Name, identity.Job, progress.Level, identity.GrowType, equipment))
        {
            IsCeremonyReserved = await ceremonies.IsReservedAsync(relation.Id, ct).ConfigureAwait(false),
        };
    }
}
