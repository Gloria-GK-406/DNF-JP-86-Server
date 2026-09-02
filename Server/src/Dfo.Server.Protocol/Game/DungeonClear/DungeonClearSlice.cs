using System.Buffers.Binary;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>
/// NOTI 0x001F <c>ENABLE_CLEAR_DUNGEON</c>: the exit opening.
/// </summary>
/// <remarks>
/// One byte, and the whole ending hangs off it. Observed live on 2026-08-26: the player killed the
/// boss room's last monster and then had nothing left to do -- no exit, no result screen, only
/// heartbeats -- because this frame was never sent. The client asks for none of what follows until
/// it has been told the dungeon may be cleared.
/// </remarks>
internal sealed record EnableClearDungeonNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnableClearDungeon;
}

/// <summary>
/// NOTI 0x0117 <c>SECRET_SHOP_NPC</c>: which merchant stands on the result screen.
/// </summary>
/// <remarks>
/// Four bytes, the NPC's own identifier, sent immediately after the exit opens. A working A21
/// session sends 1000. It is a presentation detail rather than a gate -- but it is one frame, it is
/// in the reference's order, and a result screen missing its merchant is a difference this server
/// would otherwise have to rediscover later.
/// </remarks>
internal sealed record SecretShopNpcNotification(int NpcId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSecretShopNpc;
}

/// <summary>
/// CMD 0x002E <c>SET_PLAY_RESULT</c>: the client reporting how the run went.
/// </summary>
/// <remarks>
/// Fifty-three bytes, of which the reference server reads exactly one: the rank the client has
/// worked out for itself, at offset ten. Everything else is presentation the client already owns.
/// The run's length is not in there either -- the server times it.
/// </remarks>
public sealed record SetPlayResultRequest(GameMessageMetadata Metadata, byte ClientRankPoint)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPlayResult;
}

/// <summary>NOTI 0x0022 <c>PLAY_RESULT</c>: the run's result, as the screen draws it.</summary>
internal sealed record PlayResultNotification(
    ushort WireUserId, int ClearTimeMilliseconds, byte ClientRankPoint) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePlayResult;
}

/// <summary>NOTI 0x0023 <c>CLEAR_DUNGEON_REWARD</c>: what the run paid out.</summary>
/// <param name="ClearBase">What clearing the dungeon was worth.</param>
/// <param name="RankBonus">What the run's rank added to that.</param>
/// <param name="FreeCards">
/// Every position of the clear's free card face. Carried on the frame rather than looked up by the
/// encoder, because what a clear's cards hold is fixed once at settlement and this frame reports
/// it; an encoder that went and asked would be a second place a face could be decided.
/// </param>
/// <param name="PaidFlipCost">
/// What this frame says a paid flip of the clear costs. A price rather than a nullable one, and
/// deliberately: the archive answers nothing for a level it does not price, and which of
/// <see cref="DungeonClearPayloadCodec.NoPaidFlipCost"/> and
/// <see cref="DungeonClearPayloadCodec.UnpricedPaidFlipCost"/> such a clear is reported with is a
/// decision, made by the settlement that knows which case it is in rather than defaulted here.
/// </param>
internal sealed record ClearDungeonRewardNotification(
    uint ClearBase,
    uint RankBonus,
    IReadOnlyList<ClearRewardCardSlot> FreeCards,
    int PaidFlipCost)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameClearDungeonReward;
}

/// <summary>
/// CMD 0x002A <c>GIVEUP_GAME</c>: the character leaving the dungeon.
/// </summary>
/// <remarks>
/// An empty body. It is what the client sends once the result screen is done with, and it is the
/// only thing in this server that ends a run: until 2026-08-26 nothing called
/// <c>IDungeonRunDomainService.End</c> at all, so a character who had walked back to town was
/// still, as far as the server knew, standing in a dungeon -- which is half of why that session
/// could walk back into the boss room after clearing it.
/// <para>
/// The client reached town without this being answered. What it did not get is the town state the
/// reference server pushes behind the acknowledgement -- <c>NOTI 0x0003</c>, <c>0x0017</c> and
/// <c>0x0018</c> -- which it went and asked for itself instead. Those are not sent here; see
/// <c>Docs/ProtocolScaffoldDissonances.md</c>.
/// </para>
/// </remarks>
public sealed record GiveUpGameRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGiveUpGame;
}

/// <summary>The CMD acknowledgement under the same type: one byte, as the reference sends it.</summary>
public sealed record GiveUpGameResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGiveUpGame;
}

internal sealed class GiveUpGameDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GiveUpGameRequest(metadata);
}

internal sealed class GiveUpGameResponseEncoder : GameCmdMessageEncoder<GiveUpGameResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GiveUpGameResponse message) =>
        OnePayload(GameProtocol.GiveUpGameType, new byte[] { 0x01 });
}

/// <summary>Ends the run and says so.</summary>
/// <remarks>
/// Answered whether or not there was a run to end. The client has already left; telling it no would
/// leave it holding a dungeon it is no longer in, and there is nothing it could do about that.
/// </remarks>
public sealed class GiveUpGameHandler(
    ISelectedSessionDirectory selectedSessions,
    IDungeonRunDomainService runs,
    IGameMessageSender sender,
    ILogger<GiveUpGameHandler> logger) : ClientMessageHandler<GiveUpGameRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GiveUpGameRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Give up game {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        logger.LogInformation(
            "Give up game {ConnectionId} character={CharacterId}: run ended={Ended}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            runs.End(session.CharacterId));

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new GiveUpGameResponse()], []),
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class DungeonClearPayloadCodec
{
    /// <summary>The rank the client worked out, at the one offset the reference server reads.</summary>
    public const int ClientRankPointOffset = 10;

    /// <summary>The exit opens on a single zero.</summary>
    public static byte[] EncodeEnableClearDungeon() => [0x00];

    public static byte[] EncodeSecretShopNpc(int npcId)
    {
        var body = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(body, npcId);
        return body;
    }

    /// <summary>
    /// The result, in the reference builder's order: sixteen bytes.
    /// </summary>
    /// <remarks>
    /// The clear time is written twice -- once for the run and once for the one party member the
    /// run had -- which is what the reference's own frame does with the same value in both places.
    /// The rank is echoed from the request rather than judged: the client has already drawn it, and
    /// a server that disagreed would change the letter on a screen the player is looking at.
    /// </remarks>
    public static byte[] EncodePlayResult(
        ushort wireUserId, int clearTimeMilliseconds, byte clientRankPoint)
    {
        using var stream = new MemoryStream(16);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); // presentation rank index
        writer.Write(clearTimeMilliseconds);
        writer.Write((byte)0); // time bonus point
        writer.Write(clientRankPoint);
        writer.Write((byte)0); // not a quest maze
        writer.Write((byte)1); // one member
        writer.Write(wireUserId);
        writer.Write(clearTimeMilliseconds);
        writer.Write((byte)0); // not a new best time
        return stream.ToArray();
    }

    /// <summary>
    /// The reward breakdown, in the reference builder's layout: 274 bytes.
    /// </summary>
    /// <remarks>
    /// Two of its figures carry what the clear actually paid, and the card tail carries what the
    /// clear's free face holds and what a paid flip of it costs; the rest are still zero, and zero
    /// is still the honest value for a
    /// reward this server does not grant. A clear's avatar, creature, adventure-group and premium
    /// bonuses each need a source this server has not got, and a frame carrying invented figures
    /// would put numbers on the result screen that nothing else could account for.
    /// <para>
    /// The layout is not invented. It is the reference builder's, and the reference's own 354-byte
    /// frame confirms it offset for offset: the count of per-object experience entries at 155, the
    /// list itself from 159, the free-card pair at 240 and 245, seven nine-byte card seats whose
    /// flags land on 249, 258, 267, 276, 285, 294 and 303, and the paid-card cost at 312. Ten
    /// entries account for the eighty bytes between that frame's length and this one's.
    /// </para>
    /// <para>
    /// <b>The first two fields' names are the reference server's, and are not measured.</b> Their
    /// positions are structural -- they are the frame's first eight bytes -- but which is the
    /// clear's own worth and which is the rank bonus rests on that server's naming, and its naming
    /// has been measured wrong twice on the neighbouring experience notice. A clear's worth is
    /// several times its rank bonus, so a single result screen settles it by eye; until one has
    /// been looked at, this pairing is an assumption.
    /// </para>
    /// </remarks>
    /// <param name="clearBase">What the clear itself paid.</param>
    /// <param name="rankBonus">What the run's rank added to it.</param>
    /// <param name="freeCards">
    /// Every position of this clear's free face, each naming the position it is. An empty list is
    /// a clear with no cards fixed, and writes the tail this frame carried before cards existed.
    /// </param>
    /// <param name="paidFlipCost">
    /// What a paid flip of this clear costs, written as the price it is. Zero is a price here and
    /// not an absence -- it is a flip given away -- so a caller with no price to write says which
    /// of the two cases it is in: <see cref="NoPaidFlipCost"/> for a clear that has no flip to
    /// price, <see cref="UnpricedPaidFlipCost"/> for a level the archive prices none at. The
    /// default is the first of those, which is the frame this encoder built before cards existed.
    /// </param>
    public static byte[] EncodeClearDungeonReward(
        uint clearBase,
        uint rankBonus,
        IReadOnlyList<ClearRewardCardSlot>? freeCards = null,
        int paidFlipCost = NoPaidFlipCost)
    {
        using var stream = new MemoryStream(ClearRewardBodyLength);
        using var writer = new BinaryWriter(stream);

        // Base block: the clear's four headline figures, a byte, and twenty-three reward slots.
        writer.Write(clearBase); // clear base experience
        writer.Write(checked((int)Math.Min(rankBonus, int.MaxValue))); // score bonus
        writer.Write(0u); // party clear breakdown
        writer.Write(0); // avatar bonus
        writer.Write((byte)0);
        for (var slot = 0; slot < FixedRewardSlotCount; slot++)
            writer.Write(0);

        // Two counted lists, both empty. A value written into either of these counts would be read
        // as a length, and the client would go on to consume entries that are not there.
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(0u);

        // Five fixed values the reader consumes before the score quartet.
        for (var index = 0; index < 5; index++)
            writer.Write(0);

        // The score quartet: reserved, champion, super champion, boss.
        for (var index = 0; index < 4; index++)
            writer.Write(0);

        writer.Write(0u); // no per-object experience entries

        // The card tail: the free card's pair, then seven seats, then what a paid flip would cost.
        // The face's positions fill them in order -- position zero the free-card pair, the rest
        // the seats after it -- which is the order the panel's own layout numbers them in.
        var cards = freeCards ?? [];
        writer.Write((byte)0);
        writer.Write(FreeFlipPositions(cards));
        writer.Write(ItemIdAt(cards, FreeCardPosition)); // free-card item
        writer.Write(FreeCardGoldAt(cards)); // free-card gold
        for (var seat = 0; seat < CardSeatCount; seat++)
        {
            writer.Write((byte)1);
            writer.Write(ItemIdAt(cards, seat + 1));
            writer.Write(AmountAt(cards, seat + 1));
        }

        writer.Write(paidFlipCost); // paid-card cost
        writer.Write(new byte[16]);
        writer.Write(0); // tail card item
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(0u); // the three score categories, summed; all of them zero
        writer.Write(new byte[8]);

        var body = stream.ToArray();
        if (body.Length != ClearRewardBodyLength)
        {
            throw new InvalidDataException(
                $"CLEAR_DUNGEON_REWARD is {ClearRewardBodyLength} bytes with no per-object "
                    + $"experience entries; built '{body.Length}'.");
        }

        return body;
    }

    /// <summary>What the frame is with no per-object experience entries in it.</summary>
    public const int ClearRewardBodyLength = 274;

    private const int FixedRewardSlotCount = 23;

    /// <summary>Which position of the face the free-card pair carries.</summary>
    private const int FreeCardPosition = 0;

    /// <summary>
    /// What the byte before the free-card pair declares.
    /// </summary>
    /// <remarks>
    /// Two when something sits behind the free position and one when nothing does.
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> records the byte as
    /// <c>u8 免费翻牌位数(牌后有物品时为 2,否则 1)</c> -- a value that says whether the free
    /// position has anything behind it, not a count of entries that follow.
    /// <para>
    /// <b>The pair after it is fixed either way</b>, which is what that same record's own
    /// reconciliation rests on: it accounts for the reference's 354-byte frame against this
    /// body's <see cref="ClearRewardBodyLength"/> as a flat eighty bytes of per-object experience,
    /// and the seven seat flags land on 249..303 there and 169..223 here whichever value this byte
    /// carries. So this is one value changing and nothing else: the body is the same length for
    /// both, which the check at the end of <see cref="EncodeClearDungeonReward"/> holds to.
    /// </para>
    /// </remarks>
    private static byte FreeFlipPositions(IReadOnlyList<ClearRewardCardSlot> cards) =>
        PositionOf(cards, FreeCardPosition) is null ? (byte)1 : (byte)2;

    /// <summary>What the second half of the free-card pair carries.</summary>
    /// <remarks>
    /// <b>Gold, and not the item's count.</b>
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> records the free position's pair as
    /// <c>i32 免费牌物品 / i32 免费牌金币</c> and the seven seats after it as
    /// <c>7×(u8 标记=1, i32 物品, i32 数量)</c> -- the seats carry a count, this does not. Those
    /// are the record's two layout lines, and they name different fields.
    /// <para>
    /// <b>Not relied on, and flagged rather than smoothed over:</b> that record's prose calls the
    /// reference frame's non-zero offsets 240 and 245 "the free card pair", while its own layout
    /// block puts the pair at 241 and 245 with the marker on 240. Read the second way, 241 being
    /// absent from that list is a free position carrying gold and no item, which would be a second
    /// reason this field is not a count -- but that is a reading of an offset list, not something
    /// the record states, and the record's two sentences do not agree. The layout block above is
    /// what this field is written from.
    /// </para>
    /// <para>
    /// <b>What this server actually pays on that card, and worked out the same way the payment
    /// is.</b> A flip of a position that held something puts
    /// <see cref="GameProtocol.ClearRewardCardGold"/> in the account and a flip of one that held
    /// nothing puts nothing there, so this field carries the one figure exactly when the free
    /// position holds something. It is not an independent claim about the card: reached from the
    /// same position lookup as the item beside it, so the two cannot disagree, and taken from the
    /// one place that names the figure so that settling the real rule leaves this in step.
    /// </para>
    /// <para>
    /// That is also what keeps this frame out of a shape the record does not describe. The record
    /// says nothing about a free position carrying gold and no item -- it was never reachable
    /// while this server paid no gold at all, and it is still not: gold here is conditioned on the
    /// item being there, on the same lookup the marker byte above uses, so a non-zero gold always
    /// arrives with a non-zero item and a marker of two.
    /// </para>
    /// </remarks>
    private static int FreeCardGoldAt(IReadOnlyList<ClearRewardCardSlot> cards) =>
        PositionOf(cards, FreeCardPosition) is null ? 0 : GameProtocol.ClearRewardCardGold;

    /// <summary>The nine-byte card seats that follow the free-card entry.</summary>
    /// <remarks>
    /// A flag, an identifier and an amount apiece. Their flags land on 169, 178, 187, 196, 205,
    /// 214 and 223 of this body, and the paid-card cost after the last of them on 232.
    /// </remarks>
    private const int CardSeatCount = 7;

    /// <summary>What the cost field carries on a clear that fixed no cards at all.</summary>
    /// <remarks>
    /// Zero, which is what this frame wrote there before cards existed. It is not standing in for
    /// a price the archive withheld: a settlement that fixed no cards has no paid flip at all, so
    /// there is nothing here for a price to be right or wrong about. A clear that did fix cards but
    /// names no price is the other case, and is <see cref="UnpricedPaidFlipCost"/>.
    /// </remarks>
    public const int NoPaidFlipCost = 0;

    /// <summary>
    /// What the cost field carries when a clear's settlement names no price for a paid flip.
    /// </summary>
    /// <remarks>
    /// <b>Two clears reach this, and the name only says the first.</b> The archive prices a flip by
    /// level and answers nothing for a level it does not list; and a dungeon may switch its own
    /// paid card off through <c>DungeonDefinition.OffersPaidCard</c>, which five of the archive's
    /// dungeons do, leaving the settlement with no flip for a price to be about. The settlement
    /// hands both over as one <c>null</c> -- the card slice's own refusal reason carries the same
    /// pair and argues it there -- so this field cannot tell them apart either, and does not
    /// pretend to. The constant keeps the name it was introduced under; renaming it would move a
    /// name and settle nothing.
    /// <para>
    /// <b>Not zero, and that is the whole of the decision.</b> The archive answers nothing for an
    /// unpriced level rather than zero, and <c>ClearRewardTable.GoldCardCost</c>'s own remarks say
    /// why: zero is a price -- a free flip -- and a level the table does not price is not a free
    /// one. Neither is a dungeon that has no flip on offer. This field is four bytes of price with
    /// nowhere in it to say "no price", and its width and position are the reference's, so
    /// something has to go in; what may not go in is the value that gives the flip away.
    /// </para>
    /// <para>
    /// The largest the field can carry goes in instead. Its one property is that no price in the
    /// archive's table is equal to it, so it cannot be mistaken for something read out of the
    /// file -- which is what a number written here has to be able to survive being asked about.
    /// <b>What the client does when shown it is not ascertained</b> and nothing here claims to
    /// know; the decision made is only that this server does not hand out a free flip it could
    /// not price, and that a level it could not price is visible rather than silent.
    /// </para>
    /// </remarks>
    public const int UnpricedPaidFlipCost = int.MaxValue;

    /// <summary>What the face holds at that position, or nothing.</summary>
    /// <remarks>
    /// Matched on the position each slot names rather than on where the slot sits in the list. The
    /// frame is positional, so a face handed over in some other order would otherwise have its
    /// contents written under the wrong cards, and nothing in the frame could say so.
    /// </remarks>
    private static ClearRewardCardItem? PositionOf(
        IReadOnlyList<ClearRewardCardSlot> cards, int position)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            if (cards[index].Index == position)
                return cards[index].Item;
        }

        return null;
    }

    /// <summary>The identifier that position carries, or zero when it holds nothing.</summary>
    private static int ItemIdAt(IReadOnlyList<ClearRewardCardSlot> cards, int position) =>
        PositionOf(cards, position)?.ItemId ?? 0;

    /// <summary>
    /// How many of it that position carries, or zero when it holds nothing.
    /// </summary>
    /// <remarks>
    /// The third field of a card seat, which
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> records as the <c>i32 数量</c> of
    /// <c>7×(u8 标记=1, i32 物品, i32 数量)</c> -- that seat's own count, beside that seat's own
    /// identifier. <b>The free position's second field is not this one</b>; the same record names
    /// it gold, and it is written by <see cref="FreeCardGoldAt"/>. A seat holding nothing writes
    /// the pair a clear with no cards writes, which is two zeroes.
    /// </remarks>
    private static int AmountAt(IReadOnlyList<ClearRewardCardSlot> cards, int position) =>
        PositionOf(cards, position)?.Quantity ?? 0;
}

internal sealed class SetPlayResultDecoder : IGameMessageDecoder
{
    /// <summary>Enough to carry the one field the reference server reads.</summary>
    public const int MinimumBodyLength = DungeonClearPayloadCodec.ClientRankPointOffset + 1;

    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SET_PLAY_RESULT requires at least {MinimumBodyLength} body bytes; received "
                    + $"'{body.Length}'.");
        }

        return new SetPlayResultRequest(
            metadata, body[DungeonClearPayloadCodec.ClientRankPointOffset]);
    }
}

internal sealed class EnableClearDungeonEncoder
    : GameNotiMessageEncoder<EnableClearDungeonNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        EnableClearDungeonNotification message) =>
        OnePayload(
            GameProtocol.EnableClearDungeonNotificationType,
            DungeonClearPayloadCodec.EncodeEnableClearDungeon());
}

internal sealed class SecretShopNpcEncoder : GameNotiMessageEncoder<SecretShopNpcNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        SecretShopNpcNotification message) =>
        OnePayload(
            GameProtocol.SecretShopNpcNotificationType,
            DungeonClearPayloadCodec.EncodeSecretShopNpc(message.NpcId));
}

internal sealed class PlayResultEncoder : GameNotiMessageEncoder<PlayResultNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        PlayResultNotification message) =>
        OnePayload(
            GameProtocol.PlayResultNotificationType,
            DungeonClearPayloadCodec.EncodePlayResult(
                message.WireUserId, message.ClearTimeMilliseconds, message.ClientRankPoint));
}

internal sealed class ClearDungeonRewardEncoder
    : GameNotiMessageEncoder<ClearDungeonRewardNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ClearDungeonRewardNotification message) =>
        OnePayload(
            GameProtocol.ClearDungeonRewardNotificationType,
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                message.ClearBase, message.RankBonus, message.FreeCards, message.PaidFlipCost));
}

/// <summary>
/// Pays for the finished run and answers with the result screen's frames.
/// </summary>
/// <remarks>
/// The order is the reference server's: the result, then what the character is now worth, then the
/// reward breakdown. The experience frame is the same one a kill sends and reports the same way --
/// the character's standing after the award, not a gain.
/// <para>
/// The award happens before any of them. Until 2026-08-29 a whole dungeon paid nothing: this
/// handler reported the character's entry-time total back to it and filled the reward frame with
/// zeroes.
/// </para>
/// </remarks>
public sealed class SetPlayResultHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonRunDomainService runs,
    ICardRewardDomainService cardRewards,
    ICommandBus commandBus,
    IQuestOfferRefreshDomainService offers,
    IGameMessageSender sender,
    ILogger<SetPlayResultHandler> logger) : ClientMessageHandler<SetPlayResultRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetPlayResultRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null
            || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        // Claimed, not merely looked up. The client says when a run is over, and since 2026-08-29
        // saying so pays experience -- so a repeated report would be a repeated payment. The same
        // guard a kill has, for the same reason, and the run is left in progress either way: what
        // ends it is the character walking out.
        var run = runs.TrySettle(session.CharacterId);
        if (run is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress, or "
                    + "one already settled; ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }

        // The cards are fixed here, and here is the only place they can be fixed. Above is the
        // claim: a repeated result report is turned back before it reaches this line, so one run
        // draws one pair of faces however many times its result is reported. Below is every frame
        // this settlement sends, and the reward breakdown is one of them -- so a face fixed after
        // them would be reported as the zeroes it was before it existed.
        //
        // The level handed in is the one the character cleared at, taken before the award below
        // can move it: what band a clear's cards are drawn from is a fact about the run that was
        // just fought, not about the level the last of its experience happened to buy.
        var settled = await cardRewards.SettleAsync(
            new ClearRewardSettlement(session.CharacterId, result.Character.Progression.Level),
            cancellationToken).ConfigureAwait(false);
        var freeCards = settled?.Cards.Free.Slots ?? [];

        // What the result screen is told a paid flip costs. The settlement answers the archive's
        // price for the level this clear settled at, and answers nothing for a level the archive
        // prices none at -- which is not the same as pricing it at nothing, so the two are not
        // written the same way. Neither value is invented here: both are the frame's own, named
        // and argued where the frame is built.
        var paidFlipCost = settled is null
            ? DungeonClearPayloadCodec.NoPaidFlipCost
            : settled.PaidFlipCost ?? DungeonClearPayloadCodec.UnpricedPaidFlipCost;

        var elapsed = DateTimeOffset.UtcNow - run.StartedAt;
        var clearTime = (int)Math.Clamp(elapsed.TotalMilliseconds, 0, int.MaxValue);

        // Asked before anything is paid, and the same refusal a kill makes. The experience frame
        // carries the skill-point mirror as well as the total, so without one there is no frame to
        // report the new standing on -- and paying anyway would leave the character credited, the
        // run spent, and the client still drawing the total it entered with.
        //
        // Refusing is unrecoverable here in a way it is not for a kill: SET_PLAY_RESULT arrives
        // once per run, so a clear refused is a clear never paid. That is acceptable only because
        // the branch cannot be reached for a published session -- entry refuses to publish without
        // a mirror (SelectCharacterHandler's pre-publication build throws on a missing one). If
        // that ever stops being true, this becomes a run silently paying nothing, and the choice
        // has to be made again.
        var mirror = result.CharacterEntry.Skills.ProtocolMirror;
        if (mirror is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: the session carries no "
                    + "skill point mirror, so the clear was neither paid nor reported",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [
                        new PlayResultNotification(
                            session.WireUserId, clearTime, message.ClientRankPoint),
                        new ClearDungeonRewardNotification(0, 0, freeCards, paidFlipCost),
                    ],
                    []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var paid = (await commandBus.SendAsync(
            new GrantClearExperienceCommand(
                session.AccountId,
                session.CharacterId,
                run.Layout.DungeonId,
                new ClearCircumstances(
                    run.Layout.BasisLevel,
                    run.Difficulty,
                    run.Layout.ExperienceWeight,
                    message.ClientRankPoint)),
            cancellationToken).ConfigureAwait(false));

        // The whole award, in its parts. Experience is a number nobody can check by looking, so
        // this line is what makes a live session usable as evidence for what a clear paid -- and
        // what the result screen's own figures have to agree with.
        logger.LogInformation(
            "Set play result {ConnectionId} character={CharacterId}: dungeon {DungeonId} cleared "
                + "in {ClearTime}ms at rank {Rank}; clearBase={ClearBase} rankBonus={RankBonus} "
                + "award={Award}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            run.Layout.DungeonId,
            clearTime,
            message.ClientRankPoint,
            paid.ClearBase,
            paid.RankBonus,
            paid.Award);

        // The session is corrected before the frames go out, exactly as a kill does it: every
        // later frame reads the character from here, so a session left holding the entry-time
        // total would go on reporting it.
        var award = paid.Award;
        var level = award?.Level ?? checked((byte)result.Character.Progression.Level);
        var total = award?.Total
            ?? checked((uint)result.CharacterEntry.Public.Extras.CharacterExperience);
        if (award is not null)
        {
            result = result with
            {
                Character = result.Character with
                {
                    Progression = new CharacterProgressionState(level, total),
                },
                CharacterEntry = result.CharacterEntry with
                {
                    Public = result.CharacterEntry.Public with
                    {
                        Extras = result.CharacterEntry.Public.Extras with
                        {
                            CharacterExperience = total,
                        },
                    },
                },
            };

            // A level decides which quests are offered, and a clear is as capable of crossing one
            // as a kill is -- more so, since it pays in one lump. The same refresh, for the same
            // reason: a quest whose band was just entered would otherwise stay hidden until the
            // character next entered the world.
            if (award.LeveledUp)
            {
                result = await offers
                    .RefreshAsync(result, session.AccountId, session.CharacterId, level,
                        cancellationToken).ConfigureAwait(false);
            }

            runtimeSessions.ReplaceExact(session, result);
        }

        List<IGameServerMessage> frames =
        [
            new PlayResultNotification(session.WireUserId, clearTime, message.ClientRankPoint),
            new ExperienceNotification(
                level,
                total,
                mirror,
                AccountHonorLevel.Initial,
                // Nothing a clear pays belongs to a named line: the clear's own worth and its rank
                // bonus are both what the occasion was worth, and the client draws them as the
                // base. The result screen below reports them apart.
                ExperienceBonusLines.None),
        ];

        if (award?.LeveledUp == true)
        {
            frames.Add(new AvailableQuestListMessage(
                level, result.World.Content.AvailableQuests));
        }

        // Read off the award rather than off the valuation. The command's two figures are what the
        // clear was worth; what the character received can be less -- nothing at all when the
        // award did not happen, or a clamped amount at the top of the experience table -- and the
        // result screen must report what landed. An award carries its base and its one
        // contribution, which are those same two figures whenever nothing was clamped.
        frames.Add(award is null
            ? new ClearDungeonRewardNotification(0, 0, freeCards, paidFlipCost)
            : new ClearDungeonRewardNotification(
                award.Base, award.Gained - award.Base, freeCards, paidFlipCost));

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }
}
