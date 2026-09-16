using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// Answers the layout request with the layout, whatever state the character is in.
/// </summary>
/// <remarks>
/// Unconditionally, and for the reason the scroll's own acknowledgement is unconditional: this
/// frame arrives once, at the end of a run, and the client has no way to ask again -- so a silence
/// here is indistinguishable from the server not having the type, which is the defect being fixed.
/// There is nothing to withhold either. The layout names positions and carries no item, so a
/// character who somehow sent this outside a run is shown four backs and, having no cards fixed,
/// gets nothing at all for turning one over.
/// </remarks>
public sealed class CardSelectRightStateHandler(
    ISelectedSessionDomainService selectedSessions,
    IGameMessageSender sender,
    ILogger<CardSelectRightStateHandler> logger,
    IDungeonRunDomainService? runs = null,
    DungeonCardPresentationCoordinator? cardPresentation = null)
    : ClientMessageHandler<CardSelectRightStateRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        CardSelectRightStateRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        if (session is not null
            && runs?.Find(session.CharacterId) is { } run
            && cardPresentation is not null)
        {
            await cardPresentation.OpenOnRequestAsync(
                session, run.RunId, cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Card layout {ConnectionId} character={CharacterId}: layout sent",
            context.Connection.ConnectionId,
            session?.CharacterId.Value);

        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new CardSelectRightStateResponse(),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Turns one card over and answers with the whole panel's state.
/// </summary>
/// <remarks>
/// <b>A flip that hands nothing over is answered with nothing.</b> That is the reference server's
/// behaviour and it is the right one here: the positions were revealed by the flip that did take
/// something, and the client ignores a position it has already been shown, so a second answer would
/// be a frame that changes nothing. Silence also keeps a request naming a run that does not exist
/// from being told anything about one.
/// <para>
/// <b>The acknowledgement goes first and the bag change after it, in one exchange.</b> That order is
/// the ground pick-up's, which is the reference server's: the client consumes the answer to what it
/// asked and only then reads what changed underneath it. A position that held nothing sends the one
/// frame alone -- there is no bag change to describe.
/// </para>
/// </remarks>
public sealed class SelectCardHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<SelectCardHandler> logger,
    DungeonCardSelectionProjectionDelivery? projection = null,
    DungeonCardPresentationCoordinator? cardPresentation = null)
    : ClientMessageHandler<SelectCardRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SelectCardRequest message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);

        // The published session as well as the identity, because a paid flip is priced by the
        // level the character stands at and the level is one of the things a published session
        // carries. Reading it here is a read of state this handler does not own and does not
        // change; what is done with it is the command's.
        if (session is null
            || !runtimeSessions.TryGet(session, out var published, out _)
            || published is null)
        {
            logger.LogWarning(
                "Select card {ConnectionId}: no published session, ignoring {Kind} position {Index}",
                context.Connection.ConnectionId,
                message.Kind,
                message.Index);
            return;
        }

        var run = runs.Find(session.CharacterId);
        if (run is null || selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;

        var result = await commandBus.SendAsync(
            new SelectCardCommand(
                session.AccountId,
                session.CharacterId,
                message.Kind,
                message.Index,
                run.RunId),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is not SelectCardStatus.Flipped)
        {
            logger.LogInformation(
                "Select card {ConnectionId} character={CharacterId}: {Kind} position {Index} "
                    + "handed nothing over, {Status}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                message.Kind,
                message.Index,
                result.Status);
            return;
        }

        logger.LogInformation(
            "Select card {ConnectionId} character={CharacterId}: {Kind} position {Index} taken, "
                + "{Revealed} positions revealed, bag {Bag}, charged {Charged}, paid {Paid}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.Kind,
            message.Index,
            result.Slots.Count,
            result.Delivered is null ? "unchanged" : "told",
            result.GoldCharged,
            result.GoldPaid);

        if (message.Kind is ClearRewardCardKind.Free)
            cardPresentation?.Cancel(session.CharacterId, run.RunId);

        if (projection is not null)
        {
            await projection.DeliverAsync(
                session, result, published.World.PreparedAt, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(Frames(result, published.World.PreparedAt), []),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What this flip has to tell the client: that it happened, what the bag now holds, and what
    /// the character now carries.
    /// </summary>
    /// <remarks>
    /// The gold frame goes out whenever a flip moved gold either way. Without it the player's own
    /// figure would not move until the next entry, which is the shape the whole carried-gold
    /// defect wore: the server had the right number and the client was never told it.
    /// </remarks>
    private static IReadOnlyList<IGameServerMessage> Frames(
        SelectCardResult result, DateTimeOffset preparedAt)
    {
        var frames = new List<IGameServerMessage>
        {
            new SelectCardResponse(result.Change?.Selections
                ?? [new DungeonCardSelection(result.Kind, result.Index, default, Seat: 0)]),
        };
        if (result.Delivered is { } delivered)
            frames.Add(GameInventoryDelivery.Notification(delivered));

        if (result.GoldPaid > 0 || result.GoldCharged > 0)
            frames.Add(GameInventoryDelivery.CarriedGoldNotification(result.GoldCarried, preparedAt));

        return frames;
    }
}
