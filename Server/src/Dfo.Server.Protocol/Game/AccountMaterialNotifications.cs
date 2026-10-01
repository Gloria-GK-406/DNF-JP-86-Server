using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

/// <summary>Projects account-material events; consumption ACKs already remove exhausted crystals.</summary>
internal static class AccountMaterialNotifications
{
    public static IReadOnlyList<IServerNetworkMessage> Compose(
        IReadOnlyList<InventoryRuntimeValueState> resources, DateTimeOffset preparedAt)
    {
        // A positive item ID with count zero triggers the client's obtained-items UI.
        var remaining = resources.GroupBy(resource => resource.SlotIndex)
            .Select(group => group.Last()).Where(resource => resource.Value > 0).ToArray();
        return remaining.Length == 0 ? []
            : [GameInventoryDelivery.VirtualResourceNotification(remaining, preparedAt)];
    }
}
