using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// Opens the requesting character's card panel once when the presentation coordinator is active.
/// </summary>
/// <remarks>
/// The coordinator races this request with the post-settlement layout timer. The winner opens
/// the panel and sends its layout; a later request must not replay the opening animation or
/// cancel the pending automatic flip. Without a live run/coordinator, the compatibility fallback
/// still replies with an empty-reward layout.
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
/// Confirms one personal card; the shared panel waits for all free-card confirmations.
/// </summary>
/// <remarks>
/// A refused or duplicate confirmation publishes nothing. Successful personal confirmation may
/// likewise have no shared reply yet; the local client already turned its chosen card over.
/// <para>
/// The first participant receives only private inventory and gold changes. The last confirmation
/// releases one party-ordered 0x0047 snapshot before that actor's private changes.
/// </para>
/// </remarks>
public sealed class SelectCardHandler(
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<SelectCardHandler> logger,
    DungeonCardSelectionProjectionDelivery? projection = null,
    DungeonCardPresentationCoordinator? cardPresentation = null,
    DungeonPartyRecipients? partyRecipients = null)
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
        var published = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || published is null)
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
        if (partyRecipients is not null
            && !partyRecipients.Find(session, run.RunId).Contains(session))
            return;

        var result = await commandBus.SendAsync(
            new SelectCardCommand(
                session.AccountId,
                session.CharacterId,
                message.Kind,
                message.Kind is ClearRewardCardKind.Free ? 0 : message.Index,
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
            "Select card {ConnectionId} character={CharacterId}: {Kind} wire position {Index} "
                + "confirmed, party reveal ready={RevealReady}, bag {Bag}, charged {Charged}, paid {Paid}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.Kind,
            message.Index,
            result.Change?.Selections.Count > 0,
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
        var frames = new List<IGameServerMessage>();
        if (result.Change is { Selections.Count: > 0 } change)
            frames.Add(new SelectCardResponse(change.Selections));
        if (result.Delivered is { } delivered)
            frames.Add(GameInventoryDelivery.Notification(delivered));
        if (result.AccountMaterial is { } accountMaterial)
        {
            frames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [new InventoryRuntimeValueState(
                    accountMaterial.VirtualSlotIndex,
                    accountMaterial.ItemId,
                    accountMaterial.FinalCount)],
                preparedAt));
        }

        if (result.GoldPaid > 0 || result.GoldCharged > 0)
            frames.Add(GameInventoryDelivery.CarriedGoldNotification(result.GoldCarried, preparedAt));

        return frames;
    }
}
