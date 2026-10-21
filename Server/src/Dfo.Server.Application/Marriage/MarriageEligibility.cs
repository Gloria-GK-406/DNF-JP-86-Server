using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Read-only facts about the people in a marriage: sessions, town, level, pairing and gold.</summary>
internal sealed class MarriageEligibility(ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
    ICharacterDomainService characters, IMarriageProposalRuleService proposalRules, ICharacterInventoryEntryDomainService inventory)
{
    /// <remarks>The use cases reach the session only through here (<c>MarriageArchitectureTests</c>).</remarks>
    public bool IsCurrent(SelectedSessionIdentity session) => sessions.IsInWorld(session);

    public SelectedSessionIdentity? SessionOf(CharacterId character) => sessions.FindByCharacter(character);

    public bool SameTown(SelectedSessionIdentity first, SelectedSessionIdentity second) =>
        first.ListenerPort == second.ListenerPort && areas.Find(first.CharacterId) is { SharesTownPresence: true } a
        && areas.Find(second.CharacterId) is { SharesTownPresence: true } b && a.Area == b.Area
        && a.Lease == first.TownAreaLease() && b.Lease == second.TownAreaLease();

    /// <summary>Both meet the minimum level and their jobs form an opposite-sex pair.</summary>
    public async ValueTask<bool> CanPairAsync(SelectedSessionIdentity firstSession, SelectedSessionIdentity secondSession, CancellationToken ct)
    {
        var first = await characters.Get(firstSession.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(secondSession.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var a = await characters.Get(firstSession.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        var b = await characters.Get(secondSession.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        return first is not null && second is not null && a is not null && b is not null
            && a.Level >= proposalRules.MinimumLevel && b.Level >= proposalRules.MinimumLevel && proposalRules.CanPair(first.Job, second.Job);
    }

    public async ValueTask<bool> CanAffordAsync(CharacterId character, int gold, CancellationToken ct) =>
        await characters.Get(character).GoldAsync(ct).ConfigureAwait(false) >= gold;

    /// <summary>The partner as 03F3 shows them; null when the character cannot be read.</summary>
    public async ValueTask<MarriagePartnerProfile?> ProfileAsync(SelectedSessionIdentity peer, CancellationToken ct)
    {
        var domain = characters.Get(peer.CharacterId);
        var identity = await domain.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await domain.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null) return null;
        var current = await inventory.ReadCurrentAsync(peer.AccountId, peer.CharacterId, ct).ConfigureAwait(false);
        var worn = current.Containers.SingleOrDefault(x => x.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        var equipment = Enumerable.Range(0, 13).Select(slot => worn.SingleOrDefault(x => x.SlotIndex == slot)?.Core.ItemId ?? 0).ToArray();
        return new(peer.CharacterId.Value, identity.Name, identity.Job, progress.Level, identity.GrowType, equipment);
    }
}
