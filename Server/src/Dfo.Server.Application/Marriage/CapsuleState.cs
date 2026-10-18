using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Only capsule persistence owner; the read facade never resolves marriage use cases or effects.</summary>
internal sealed class CapsuleState(IMarriageCapsuleDomainStore store, IMarriageRelations relations) : IMarriageCapsuleQueryDomainService
{
    public ValueTask<MarriageCapsuleState> ReadAsync(Guid relation, CharacterId character, CancellationToken ct) => store.ReadAsync(relation, character, ct);
    public ValueTask SaveAsync(Guid relation, CharacterId character, MarriageCapsuleState value, CancellationToken ct) => store.SaveAsync(relation, character, value, ct);
    public ValueTask<bool> CreditAsync(Guid relation, CharacterId character, DungeonRunId run, int gain, int maximum, CancellationToken ct) => store.CreditAsync(relation, character, run, gain, maximum, ct);
    public async ValueTask<MarriageCapsuleSnapshot> ReadAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await relations.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation) return new(new());
        var peer = relation.Proposer.CharacterId == character ? relation.Recipient : relation.Proposer;
        return new(await ReadAsync(relation.Id, character, ct).ConfigureAwait(false), (await ReadAsync(relation.Id, peer.CharacterId, ct).ConfigureAwait(false)).Pending > 0);
    }
}
