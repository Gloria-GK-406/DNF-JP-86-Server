using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
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
    ITutorialRewardDefinitionSource rewards,
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

        // Nothing is granted yet: there is no seam that inserts an arbitrary item into a
        // character's inventory, only the daily refill's own path. The acknowledgement reports
        // what was granted rather than what was declared, so an empty list here is accurate rather
        // than optimistic -- and it is the correct answer for every flag observed so far, none of
        // which declares a reward. The one flag that does has never been seen to fire.
        var granted = Array.Empty<GrantedTutorialItem>();
        if (declared.Count > 0)
        {
            logger.LogWarning(
                "Tagged id report {ConnectionId}: tutorial flag {Flag} declares {Count} reward "
                    + "item(s) that cannot be granted yet; acknowledging with none",
                context.Connection.ConnectionId,
                message.Id,
                declared.Count);
        }

        logger.LogInformation(
            "Tagged id report {ConnectionId} {Attribution}: id={Id} flag={Flag} declared={Declared} granted={Granted}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.Id,
            message.Flag,
            declared.Count,
            granted.Length);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new TutorialFlagAcknowledgement(granted)], []),
            cancellationToken).ConfigureAwait(false);
    }
}
