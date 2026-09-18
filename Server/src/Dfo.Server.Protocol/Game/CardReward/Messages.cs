using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>
/// CMD 0x0046 <c>CARD_SELECT_RIGHT_STATE</c>: the client asking for the card panel's layout.
/// </summary>
/// <remarks>
/// An empty body. The frame is built inside the client's own handler for the <c>0x0045</c> reply
/// -- the packer sits at RVA <c>0x00D0E4D1</c>, which is inside the reply handler at
/// <c>0x00D0E410</c> -- so it arrives immediately after the result scroll is acknowledged and only
/// on the branch that opens the card panel. See <c>protocol/game/client-to-server/0x0046.md</c> for
/// the closed layout and <c>0x0045.md</c> for the branch.
/// </remarks>
public sealed record CardSelectRightStateRequest(GameMessageMetadata Metadata)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCardSelectRightState;
}

/// <summary>
/// CMD 0x0047 <c>SELECT_CARD</c>: the player turning one card of one face over.
/// </summary>
/// <remarks>
/// Two bytes: the face and a wire position. On the free-card path the position is not the local
/// visual column; the server confirms the character's personal result at position zero. See
/// <see cref="CardRewardPayloadCodec"/> for the two readings that produce this, and for why the
/// free one and the paid one are read apart.
/// </remarks>
public sealed record SelectCardRequest(
    GameMessageMetadata Metadata, ClearRewardCardKind Kind, int Index) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectCard;
}

/// <summary>
/// The CMD answer under the same type: the whole panel's state after the flip.
/// </summary>
/// <remarks>
/// <b>It carries no reward.</b> What a card holds was settled and sent with the clear's reward
/// notification; this answer says which column of which row is now face up and whose flip it was.
/// The player's own client decides which column to turn, and never tells the server which one --
/// the request's position byte is a written-in constant. See
/// <see cref="CardRewardPayloadCodec.EncodeSelectCardResponse"/>.
/// </remarks>
/// <param name="Selections">The party-ordered ownership snapshot after collective reveal.</param>
public sealed record SelectCardResponse(IReadOnlyList<DungeonCardSelection> Selections)
    : IGameCmdMessage
{
    public SelectCardResponse(ClearRewardCardKind kind, int turnedColumn)
        : this([new DungeonCardSelection(kind, turnedColumn, default, Seat: 0)])
    {
    }

    /// <summary>Compatibility view for single-selection callers and diagnostics.</summary>
    public ClearRewardCardKind Kind => this.Selections.Count == 0
        ? ClearRewardCardKind.Free
        : this.Selections[^1].Kind;

    /// <summary>Compatibility view for single-selection callers and diagnostics.</summary>
    public int TurnedColumn => this.Selections.Count == 0 ? -1 : this.Selections[^1].Index;

    public ServerMessageType MessageType => ServerMessageType.GameSelectCard;
}
