using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game;

// Endpoint-owned delivery composes existing outbound slices without coupling their callers.
public sealed class CharacterSelectionDelivery(IGameMessageSender sender, ICleanupNotificationDelivery cleanupDelivery)
{
    public ValueTask DeliverCleanupAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken cancellationToken) =>
        cleanupDelivery.DeliverAsync(plan, cancellationToken);

    public async ValueTask SendAsync(NetworkConnectionId connectionId, IGameCmdMessage acknowledgement,
        AccountCharacterRoster roster, bool resetKnightDeck, CancellationToken cancellationToken)
    {
        if (resetKnightDeck)
            await sender.SendNotiAsync(connectionId, new KnightShieldDeckMessage([0, 0, 0, 0, 0]),
                cancellationToken).ConfigureAwait(false);
        await sender.SendCmdAsync(connectionId, acknowledgement, cancellationToken).ConfigureAwait(false);
        await sender.SendNotiAsync(connectionId, new AccountCharacterRosterResponse(roster, AnswersRosterQuery: false),
            cancellationToken).ConfigureAwait(false);
        await sender.SendNotiAsync(connectionId, new HonorLevelInfoNotice(AccountHonorLevel.Initial),
            cancellationToken).ConfigureAwait(false);
    }
}
