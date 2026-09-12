using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Parties;

/// <summary>
/// Reads party UI values from the domains that own them.
/// </summary>
/// <remarks>
/// Revive coins are a character inventory virtual resource and fatigue belongs to its dedicated
/// service. HP is the current selected-session projection already sent through USERINFO. Stamina
/// and anti-magic deliberately remain zero until their gameplay models have authoritative writers.
/// </remarks>
internal sealed class PartyMemberStatusDomainService(
    Func<ICharacterInventoryEntryDomainService> inventories,
    ICharacterFatigueDomainService fatigue,
    ISelectedSessionRuntimeDirectory runtimeSessions) : IPartyMemberStatusDomainService
{
    private const int ReviveCoinSlot = 1;
    private const int ReviveCoinItemId = 1;

    public async ValueTask<PartyInviteDisplayState> InvitationOfAsync(
        SelectedSessionIdentity session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var inventory = await inventories().LoadAsync(
                session.AccountId, session.CharacterId, cancellationToken)
            .ConfigureAwait(false);
        var reviveCoins = inventory.VirtualResources.SingleOrDefault(
            value => value.SlotIndex == ReviveCoinSlot);
        if (reviveCoins is not null && reviveCoins.ItemId != ReviveCoinItemId)
        {
            throw new InvalidDataException(
                $"Party invitation found item {reviveCoins.ItemId} in revive-coin slot {ReviveCoinSlot}.");
        }

        var balance = await fatigue.BalanceOfAsync(session.CharacterId, cancellationToken)
            .ConfigureAwait(false);
        return new PartyInviteDisplayState(
            reviveCoins?.Value ?? 0,
            balance.Remaining,
            Stamina: 0,
            AntiMagic: 0);
    }

    public byte CurrentHpPercentOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!runtimeSessions.TryGet(session, out var result, out _)
            || result is null
            || result.Character.Identity.CharacterId != session.CharacterId)
        {
            return 0;
        }

        return checked((byte)Math.Clamp(
            result.CharacterEntry.Public.DerivedStats.CurrentHpMpPercent,
            0,
            100));
    }
}
