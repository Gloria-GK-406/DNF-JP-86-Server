using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Initialization;
internal sealed class InitializationAssetGrants(IAccountWalletDomainService wallets, IPremiumServiceDomainService premiums,
    IInventoryLevelDomainService levels, ICharacterDomainService characters, IItemGrantDomainService items)
{
    public async ValueTask AccountAsync(AccountId account, AccountInitialization input, CancellationToken ct)
    {
        if (input.Cera > 0) await wallets.AddAsync(account, AccountCurrency.Cera, input.Cera, ct);
        if (input.TokenCera > 0) await wallets.AddAsync(account, AccountCurrency.TokenCera, input.TokenCera, ct);
        if (input.HappyTokenCera > 0) await wallets.AddAsync(account, AccountCurrency.HappyTokenCera, input.HappyTokenCera, ct);
        foreach (var c in input.Contracts) if (c.Days > 0) await premiums.For(account).AddDaysAsync((PremiumServiceType)c.Type, c.Days, ct);
        if (input.AccountCargoLevel > 0) await levels.SetLevelAsync(ContainerId.ForAccountCargo(account), input.AccountCargoLevel, ct);
    }
    public async ValueTask CharacterAsync(CharacterId character, CharacterInitialization input, CancellationToken ct)
    {
        if (input.Level != 1) await characters.Get(character).SetLevelAsync( checked((byte)input.Level), ct);
        await levels.SetLevelAsync(ContainerId.ForCharacter(character, InventoryContainerType.PersonalCargo), input.PersonalCargoLevel, ct);
        foreach (var item in input.Items) await items.GrantAllPersistentAsync(character, item.ItemId, item.Count, ct);
    }
}
