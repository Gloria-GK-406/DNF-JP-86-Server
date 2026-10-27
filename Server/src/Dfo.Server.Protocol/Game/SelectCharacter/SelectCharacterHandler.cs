using Dfo.Server.Abstractions.DomainEvents;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// Whether the frames that follow the entry batch are put on the wire.
/// </summary>
/// <remarks>
/// The entry batch ends with NOTI 0x007C <c>ENTER_GAMEWORLD_COMPLETE</c>, an eight-byte signal
/// telling the client that world entry is over.
///
/// This flag was added when the A12 reference sequence was the only capture available, and that
/// sequence does stop at the marker. A21 does not: its entry sequence carries twelve more frames
/// after 0x007C, and four of them are the pre-publication frames built here -- NOTI 0x0165 user
/// visibility, CMD 0x0239 clone title, the NOTI 0x0002 subtype-0x02 character record, and NOTI
/// 0x0289 honor progress -- followed by the trailing equipment-lock snapshot and a second
/// mercenary answer. The earlier note recorded on this member, that the client dies while reading
/// that tail, does not survive the A21 capture: the client dies when the tail is *withheld*,
/// because the subtype-0x02 record is what it builds its own town avatar from.
///
/// On in production for A21, and the tests that assert the delivery contract keep asserting it.
/// The commands run either way -- they do the domain work, and publication in particular is what
/// makes the session readable to the dungeon-selection handler. Only the frames are withheld.
/// </remarks>
public sealed record SelectCharacterDeliveryOptions(bool SendsFramesAfterTheCompletionMarker)
{
    /// <summary>Matches the A12 reference sequence: nothing after the completion marker.</summary>
    /// <remarks>
    /// Retained because it describes a real captured sequence, not because it is the current
    /// production choice. A21 sends a post-marker tail; see the remarks on this type.
    /// </remarks>
    public static SelectCharacterDeliveryOptions ReferenceSequence { get; } = new(false);

    /// <summary>The post-marker tail on the wire, which is what A21 does.</summary>
    public static SelectCharacterDeliveryOptions Everything { get; } = new(true);
}

public sealed class SelectCharacterHandler(ICommandBus commands) : ClientMessageHandler<SelectCharacterRequest>, IProtocolCommandMessageHandler<GameSelectCharacterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SelectCharacterRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameSelectCharacterCommand(context, message), cancellationToken).ConfigureAwait(false);
}
