using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>
/// Consumes the five-byte 0x008F report and acknowledges it with whatever the step gave.
/// </summary>
/// <remarks>
/// The message has two call sites in the client and they are not the same code, which the flow
/// trace separated by send stack: one is the tutorial, which sends it inside the burst that also
/// asks to enter the dungeon, and one is the exit path, which sends it while handling the EXIT
/// response. The handler therefore tolerates an unattributed connection -- on the exit path the
/// connection may already be closing.
///
/// The acknowledgement is where the tutorial's rewards travel. The reference server answers every
/// flag with a result byte and the list of items it inserted, so a step that gives nothing still
/// gets an answer with a count of zero. That was measured to be safe to omit -- the client
/// continues without it -- but omitting it is also why no tutorial reward has ever been handed
/// over here.
/// </remarks>
public sealed class TaggedIdReportHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDirectory selectedSessions,
    ITutorialRewardDefinitionSource rewards,
    IItemPlacementDefinitionSource placements,
    IEntryInventoryMutationRepository inventory,
    IGameMessageSender sender,
    ILogger<TaggedIdReportHandler> logger) : ClientMessageHandler<TaggedIdReportRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        TaggedIdReportRequest message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        var declared = await rewards.GetAsync(message.Id, cancellationToken).ConfigureAwait(false);

        // The acknowledgement reports what reached the inventory, not what the archive declares.
        // An item that was refused -- unknown to the archive, or its band already full -- is left
        // out, because naming it here would tell the client to draw something the server does not
        // hold.
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var granted = new List<GrantedTutorialItem>(declared.Count);
        foreach (var item in declared)
        {
            if (session is null)
            {
                logger.LogWarning(
                    "Tagged id report {ConnectionId}: flag {Flag} gives item {ItemId} but no "
                        + "character is selected, skipping",
                    context.Connection.ConnectionId, message.Id, item.ItemId);
                continue;
            }

            var placement = await placements.GetAsync(item.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (placement is null)
            {
                logger.LogWarning(
                    "Tagged id report {ConnectionId}: flag {Flag} gives item {ItemId}, which the "
                        + "archive declares nowhere to put, skipping",
                    context.Connection.ConnectionId, message.Id, item.ItemId);
                continue;
            }

            var placed = await inventory.GrantItemAsync(
                    session.CharacterId, item.ItemId, item.Count, placement, cancellationToken)
                .ConfigureAwait(false);
            if (placed is null)
            {
                logger.LogWarning(
                    "Tagged id report {ConnectionId}: flag {Flag} gives item {ItemId} but slots "
                        + "{Start}-{End} are full, skipping",
                    context.Connection.ConnectionId, message.Id, item.ItemId,
                    placement.SlotStart, placement.SlotEnd);
                continue;
            }

            granted.Add(new GrantedTutorialItem(
                checked((ushort)placed.SlotIndex), (uint)placed.ItemId, (uint)placed.Count));
        }

        logger.LogInformation(
            "Tagged id report {ConnectionId} {Attribution}: id={Id} flag={Flag} declared={Declared} granted={Granted}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.Id,
            message.Flag,
            declared.Count,
            granted.Count);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new TutorialFlagAcknowledgement(granted)], []),
            cancellationToken).ConfigureAwait(false);
    }
}
