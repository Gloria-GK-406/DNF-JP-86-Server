using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

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
    IOnlineCharacterRuntimeDomainService onlineCharacters) : IPartyMemberStatusDomainService
{
    private const int ReviveCoinSlot = 1;
    private const int ReviveCoinItemId = 1;

    public async ValueTask<PartyInviteDisplayState> InvitationOfAsync(
        PartyCharacterSnapshot character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        var inventory = await inventories().LoadAsync(
                character.AccountId, character.CharacterId, cancellationToken)
            .ConfigureAwait(false);
        var reviveCoins = inventory.VirtualResources.SingleOrDefault(
            value => value.SlotIndex == ReviveCoinSlot);
        if (reviveCoins is not null && reviveCoins.ItemId != ReviveCoinItemId)
        {
            throw new InvalidDataException(
                $"Party invitation found item {reviveCoins.ItemId} in revive-coin slot {ReviveCoinSlot}.");
        }

        var balance = await fatigue.BalanceOfAsync(character.CharacterId, cancellationToken)
            .ConfigureAwait(false);
        return new PartyInviteDisplayState(
            reviveCoins?.Value ?? 0,
            balance.Remaining,
            Stamina: 0,
            AntiMagic: 0);
    }

    public byte CurrentHpPercentOf(CharacterId characterId) => onlineCharacters.HpPercentOf(characterId);
}
