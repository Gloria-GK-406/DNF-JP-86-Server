using System.Collections.Immutable;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Inventory;

public sealed record AvatarCompoundInput(BusinessSlot Material, BusinessSlot First, int FirstItemId,
    BusinessSlot Second, int SecondItemId, int TargetItemId, ushort Ability, byte RandomTarget, byte Category);
public sealed record AvatarCompoundPlusIngredient(BusinessSlot Slot, int ItemId);
public sealed record AvatarCompoundPlusInput(BusinessSlot Material,
    ImmutableArray<AvatarCompoundPlusIngredient> Ingredients, int TargetItemId, ushort Ability)
{
    public const int IngredientCount = 8;
    public static bool SupportsConverter(int itemId) => itemId is 10007246 or 10007247;
}
public enum AvatarCompoundRejection { None, InvalidRequest, NoSession, MissingMaterial, InvalidAvatar, Locked,
    UnsupportedLifetime, UnsupportedContent, InvalidChoice, NoRoom, StorageFailure }
public sealed record AvatarCompoundOutput(BusinessSlot Slot, ItemSnapshot Item);
public sealed record AvatarCompoundDeduction(BusinessSlot Slot, int Count);
public sealed record AvatarCompoundReceipt(SelectedSessionIdentity Session,
    ImmutableArray<AvatarCompoundDeduction> Deductions, ImmutableArray<AvatarCompoundOutput> Outputs,
    int RareRate, DateTimeOffset At)
{
    public ItemSnapshot? MaterialRemaining { get; init; }
}
public sealed record AvatarCompoundResult(AvatarCompoundRejection Rejection, AvatarCompoundReceipt? Receipt = null);
public sealed record AvatarCompoundPlan(AvatarCompoundRejection Rejection,
    ImmutableArray<ItemSnapshot> Outputs = default, int MaterialCount = 0, int RareRate = 0);
