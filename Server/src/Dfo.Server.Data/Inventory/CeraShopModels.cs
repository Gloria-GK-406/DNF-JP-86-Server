using System.Collections.Immutable;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public sealed record CeraShopAvatarChoice(int ItemId, byte Ability);
public sealed record CeraShopSelection(int PackageItemId, ushort GroupIndex, ushort SelectionIndex);
public sealed record CeraShopPurchaseEntry(byte Mode, byte Attribute, byte Variant, int ProductId,
    ImmutableArray<CeraShopAvatarChoice> AvatarChoices, ImmutableArray<CeraShopSelection> Selections,
    int CouponItemId = 0, ushort CouponSlot = 0);
public sealed record CeraShopPurchaseInput(byte Header0, byte Header1, byte CouponIndicator,
    ImmutableArray<CeraShopPurchaseEntry> Entries, string? InvalidReason = null);

public enum CeraShopRejection
{
    None, NoSession, InvalidRequest, UnknownProduct, UnsupportedProduct, InvalidChoice,
    PurchaseLevel, InsufficientFunds, NoRoom, StateChanged, StorageFailure,
}

public sealed record CeraShopPurchaseReceipt(SelectedSessionIdentity Session, ImmutableArray<int> ProductIds,
    AccountCeraBalances Balances, int Gold, bool GoldSpent, ImmutableArray<CommittedInventoryUpdateState> Updates,
    ImmutableArray<CharacterPetRuntimeState> Pets, bool CreaturesChanged, DateTimeOffset PreparedAt);

public sealed record CeraShopPurchaseResult(CeraShopRejection Rejection, CeraShopPurchaseReceipt? Receipt, string? Detail)
{
    public static CeraShopPurchaseResult Refused(CeraShopRejection rejection, string? detail = null) => new(rejection, null, detail);
}
