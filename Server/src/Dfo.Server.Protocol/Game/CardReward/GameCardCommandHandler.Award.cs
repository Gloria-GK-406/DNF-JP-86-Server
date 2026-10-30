using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.CardReward;
internal sealed partial class GameCardCommandHandler
{
    private ValueTask<SelectCardResult> AwardAsync(CommandExecutionContext execution, AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int index, DungeonRunId expectedRunId, CancellationToken token) =>
        units.WriteAsync(execution, new("select-card", []),
            (_, ct) => AwardBodyAsync(accountId, characterId, kind, index, expectedRunId, ct), token);
    internal async ValueTask<SelectCardResult> AwardBodyAsync(AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int index, DungeonRunId expectedRunId, CancellationToken cancellationToken)
    {
        var flipped = await flips.FlipAsync(accountId, characterId, kind, index, expectedRunId, cancellationToken)
            .ConfigureAwait(false);
        if (flipped.Status is not SelectCardStatus.Flipped)
            return SelectCardResult.Refused(flipped.Status, kind, index);
        var delivery = Project(flipped.Delivered);
        var bonus = Project(flipped.BlackDiamondDelivered);
        return new SelectCardResult(
            SelectCardStatus.Flipped,
            kind,
            index,
            flipped.Slots,
            delivery.CharacterItem,
            flipped.GoldCharged,
            flipped.GoldPaid,
            flipped.GoldCarried,
            StaticChange: null,
            SelectionCommit: flipped.SelectionCommit,
            AccountMaterial: delivery.AccountMaterial)
        {
            Entitlements = flipped.Cost!.Entitlements, Usage = flipped.Cost.Usage,
            BlackDiamondDelivered = bonus.CharacterItem,
            BlackDiamondAccountMaterial = bonus.AccountMaterial,
        };
    }

    /// <summary>The landed piece as the client's inventory update and shared-material value.</summary>
    private CardItemDelivery Project(IReadOnlyList<ItemDeliveryAddition> landed)
    {
        var ordinary = landed.Where(x => x.Slot.Space != BusinessInventorySpace.SharedMaterial)
            .GroupBy(x => x.Slot).Select(x => x.Last()).ToArray();
        var shared = landed.LastOrDefault(x => x.Slot.Space == BusinessInventorySpace.SharedMaterial);
        return new CardItemDelivery(ordinary.Length == 0 ? null : new CommittedInventoryUpdateState(
            A21InventoryLayout.ToGameSlot(ordinary[0].Slot).Space,
            ordinary.Select(x => new InventoryItemState(null, A21InventoryLayout.ToGameSlot(x.Slot).Index, x.Snapshot.ToCore())).ToArray(), clock.GetUtcNow()),
            shared is null ? null : new InventoryRuntimeValueState(A21InventoryLayout.ToGameSlot(shared.Slot).Index, shared.Snapshot.ItemId, shared.Snapshot.Count));
    }

    private sealed record CardItemDelivery(
        CommittedInventoryUpdateState? CharacterItem,
        InventoryRuntimeValueState? AccountMaterial);
}
