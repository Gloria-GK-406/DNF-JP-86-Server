using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Trade;

internal static class TradeProjection
{
    internal static async ValueTask SendClosureAsync(ISelectedSessionDomainService sessions, IGameCommandOutbox outbox,
        ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts, TimeProvider clock,
        IPlayerTradeSession trade, bool settled, CancellationToken ct)
    {
        if (!trade.Accepted)
        {
            await SendAsync(sessions, outbox, trade.Inviter, TradePackets.Refused(), ct).ConfigureAwait(false);
            return;
        }

        foreach (var member in trade.Participants)
        {
            if (!sessions.IsInWorld(member)) continue;
            var inventory = await inventories.ReadCurrentAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false);
            var account = await accounts.LoadAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false);
            var main = inventory.Containers.Single(container => container.ContainerType == InventoryContainerType.Main);
            await outbox.RegisterExplicitAsync(member.ConnectionId, new(
                [TradePackets.Closed(settled), new InventoryFullSnapshotMessage(main, inventory, account, clock.GetUtcNow())],
                []), ct).ConfigureAwait(false);
        }
    }

    internal static ValueTask SendAsync(ISelectedSessionDomainService sessions, IGameCommandOutbox outbox,
        SelectedSessionIdentity recipient, IGameServerMessage message, CancellationToken ct) =>
        sessions.IsInWorld(recipient)
            ? outbox.RegisterExplicitAsync(recipient.ConnectionId, new([message], []), ct)
            : ValueTask.CompletedTask;

    internal static IReadOnlyList<SelectedSessionRecipient> Recipients(SelectedSessionIdentity actor,
        SelectedSessionIdentity other, NetworkConnectionContext connection, GameMessageMetadata metadata) =>
        [new(actor, connection, metadata), new(other, null)];
}
