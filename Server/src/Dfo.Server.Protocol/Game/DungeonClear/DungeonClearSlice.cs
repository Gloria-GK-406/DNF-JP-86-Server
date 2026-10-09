using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Party;
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
    ushort WireUserId,
    int ClearTimeMilliseconds,
    byte ClientRankPoint,
    bool QuestMaze = false) : IGameNotiMessage
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
internal sealed record PartyCardReward(byte Seat, IReadOnlyList<ClearRewardCardSlot> Cards, int Gold)
{
    public ClearRewardCardFace? BlackDiamond { get; init; }
}

internal sealed record ClearDungeonRewardNotification(
    uint ClearBase,
    uint RankBonus,
    IReadOnlyList<ClearRewardCardSlot> FreeCards,
    int CardGold,
    int PaidFlipCost,
    IReadOnlyList<PartyCardReward>? PartyCards = null,
    DungeonKillExperienceSummary? KillExperience = null,
    ExperienceBreakdown? Experience = null,
    ClearRewardCardFace? BlackDiamond = null)
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
/// The acknowledgement is followed by the same town projection used by EPLP and cancelling the
/// selection screen. Ending server state without that projection leaves the client drawing a
/// dungeon whose run no longer exists, so the two changes are one exit operation.
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
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterPositionDomainService positions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townAreaProjection,
    ILogger<GiveUpGameHandler> logger,
    SelectedSessionTransitionGate transitionGate,
    ITownAreaDomainService? townAreas = null,
    ICommandBus? commands = null,
    PartyNotificationDelivery? partyNotifications = null,
    DungeonCardPresentationCoordinator? cardPresentation = null,
    IllusionTowerProjectionDelivery? illusionTower = null,
    IllusionTowerPartyCoordinator? towerParties = null)
    : ClientMessageHandler<GiveUpGameRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GiveUpGameRequest message,
        CancellationToken cancellationToken)
    {
        if (IllusionTowerProjectionDelivery.Stale(message.Metadata))
            return;
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Give up game {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            if (towerParties is not null)
            {
                var towerAttempt = await towerParties.TryLeaveAsync(
                    session, allowVirtual: true, new GiveUpGameResponse(), cancellationToken)
                    .ConfigureAwait(false);
                if (towerAttempt.Handled)
                {
                    logger.LogInformation(
                        "Give up game {ConnectionId} character={CharacterId}: tower ended={Ended}",
                        context.Connection.ConnectionId, session.CharacterId.Value,
                        towerAttempt.Result.Succeeded);
                    return;
                }
            }
            else if (illusionTower is not null
                && await illusionTower.LeaveAndReturnAsync(
                    context.Connection.ConnectionId, message.Metadata,
                    new GiveUpGameResponse(), cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var result = await currentViews.TryLoadAsync(session, cancellationToken)
                .ConfigureAwait(false);
            if (result is null || positions.Find(session.CharacterId) is not { } position)
            {
                logger.LogWarning(
                    "Give up game {ConnectionId}: no published session, ignoring",
                    context.Connection.ConnectionId);
                return;
            }

            var ended = false;
            var reroute = false;
            PartyCommandExecution? partyExecution = null;
            await using (var transition = await transitionGate.AcquireAsync(
                session.AccountId, UserAccessMode.Exclusive, cancellationToken)
                .ConfigureAwait(false))
            {
                if (selectedSessions.FindByConnection(context.Connection.ConnectionId) != session)
                    return;
                if (towerParties?.HasInvolvedTower(session) == true)
                {
                    reroute = true;
                }
                else
                {
                    var currentRun = runs.Find(session.CharacterId);
                    if (currentRun is not null)
                        cardPresentation?.Cancel(session.CharacterId, currentRun.RunId);
                    ended = currentRun is not null
                        && await runs.EndAsync(session.CharacterId, currentRun.RunId)
                            .ConfigureAwait(false);
                    if (ended && currentRun is not null)
                        deliveries.RemoveParticipant(currentRun.RunId, session.CharacterId);
                    if (ended && townAreas is not null && commands is not null)
                    {
                        var partyResult = await commands.SendAsync(
                            new LeavePartyCommand(session.CharacterId),
                            cancellationToken).ConfigureAwait(false);
                        partyExecution = new PartyCommandExecution(
                            partyResult,
                            PartyNotificationRecipientCapture.Capture(
                                partyResult.Notifications, selectedSessions, townAreas));
                    }
                }
            }

            if (reroute)
            {
                if (attempt >= 7)
                    return;
                continue;
            }

            if (partyExecution is { Result.Succeeded: true } && partyNotifications is not null)
            {
                await partyNotifications.DeliverAsync(
                    partyExecution.Result.Notifications,
                    partyExecution.Recipients,
                    cancellationToken).ConfigureAwait(false);
            }
            logger.LogInformation(
                "Give up game {ConnectionId} character={CharacterId}: run ended={Ended}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                ended);

            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery([new GiveUpGameResponse()], []),
                cancellationToken).ConfigureAwait(false);
            await townAreaProjection.EnterAsync(
                [new TownAreaEntry(session, result, position)],
                cancellationToken).ConfigureAwait(false);
            return;
        }
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
        ushort wireUserId,
        int clearTimeMilliseconds,
        byte clientRankPoint,
        bool questMaze = false)
    {
        using var stream = new MemoryStream(16);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); // presentation rank index
        writer.Write(clearTimeMilliseconds);
        writer.Write((byte)0); // time bonus point
        writer.Write(clientRankPoint);
        writer.Write(questMaze ? (byte)1 : (byte)0);
        writer.Write((byte)1); // one member
        writer.Write(wireUserId);
        writer.Write(clearTimeMilliseconds);
        writer.Write((byte)0); // not a new best time
        return stream.ToArray();
    }

    /// <summary>
    /// The reward breakdown: the clear's figures, then the card panel's three rows.
    /// </summary>
    /// <remarks>
    /// Two of its figures carry what the clear actually paid, and the card tail carries what the
    /// clear's free face holds and what a paid flip of it costs; the rest are still zero, and zero
    /// is still the honest value for a
    /// reward this server does not grant. A clear's avatar, creature, adventure-group and premium
    /// bonuses each need a source this server has not got, and a frame carrying invented figures
    /// would put numbers on the result screen that nothing else could account for.
    /// <para>
    /// <b>The layout is measured, not transcribed.</b> Its length is
    /// <c>210 + 8N + 8E</c> for <c>N</c> per-object experience entries and <c>E</c> card entries
    /// across the three rows, and five frames of three different lengths close on it -- an empty
    /// face at 274, this server's own at 282, and the reference's 354-byte capture. Three of the
    /// fields in front of the tail are counts, so nothing here may reach a fixed offset: see
    /// <c>protocol/game/server-to-client/NOTI-0x0023.md</c> for the walk and for the live runs
    /// that fixed each part of the tail's meaning.
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
        int cardGold = 0,
        int paidFlipCost = NoPaidFlipCost,
        IReadOnlyList<PartyCardReward>? partyCards = null,
        DungeonKillExperienceSummary? killExperience = null,
        ExperienceBreakdown? experience = null,
        ClearRewardCardFace? blackDiamond = null)
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
            writer.Write(slot switch
            {
                1 => experience?.SpiritPotion ?? 0u,
                2 => experience?.BlackDiamond ?? 0u,
                9 => experience?.GrowthContract ?? 0u,
                18 => killExperience?.GrowthContract ?? 0u, // Already awarded kills; display only.
                _ => 0u,
            });

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
        var kills = killExperience ?? new();
        writer.Write(0);
        writer.Write(kills.Champion);
        writer.Write(kills.SuperChampion);
        writer.Write(kills.Boss);

        writer.Write(0u); // no per-object experience entries

        // The card tail: a record count, then one group per row of the panel with the paid flip's
        // cost between the first two, then the trailer. Three rows, eight columns apiece; the
        // clear's reward goes on the free row and the other two rows are declared empty.
        var cards = freeCards ?? [];
        writer.Write((byte)0); // no item-acquisition records
        WriteFreeRow(writer, cards, cardGold, partyCards);
        writer.Write(paidFlipCost); // paid-card cost
        WriteUnturnedRow(writer); // the gold row
        var bonusEntries = WriteBlackDiamondRow(writer, blackDiamond, partyCards);

        // The twenty-two byte trailer, field for field as the client reads it: two figures into
        // protected globals, a UI branch switch, a flag, and three more figures. All zero.
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var body = stream.ToArray();
        var expected = partyCards is null ? ClearRewardBodyLengthFor(freeCards)
            : ClearRewardBodyLength + partyCards.Count(card => RewardOf(card.Cards) is not null) * CardEntryLength;
        expected += bonusEntries * CardEntryLength;
        if (body.Length != expected)
        {
            throw new InvalidDataException(
                $"CLEAR_DUNGEON_REWARD with no per-object experience entries and "
                    + $"{(RewardOf(cards) is null ? "nothing" : "something")} behind the card is "
                    + $"'{expected}' bytes; built '{body.Length}'.");
        }

        return body;
    }

    /// <summary>
    /// One row of the card tail: eight columns, each declaring how much it holds.
    /// </summary>
    /// <remarks>
    /// <b>A column's count byte is an entry count, not a flag.</b> Each entry is an identifier and
    /// an amount, and the client pushes every one of them into a list it keeps per column and row,
    /// so a column can declare more than one. What it does with them is fixed by position:
    /// <list type="bullet">
    /// <item>the first entry is that card's <b>gold</b>, and its identifier is not read at all;</item>
    /// <item>the second entry is that card's <b>piece</b>, drawn as an item;</item>
    /// <item>a third and later entry is consumed but never drawn.</item>
    /// </list>
    /// Live runs establish each of those: writing a real identifier in the first entry still
    /// displayed its amount as gold, writing a piece in the second displayed that piece, and a
    /// third entry never appeared. See <c>protocol/game/server-to-client/NOTI-0x0023.md</c>.
    /// </remarks>
    private static void WriteFreeRow(
        BinaryWriter writer, IReadOnlyList<ClearRewardCardSlot> cards, int cardGold,
        IReadOnlyList<PartyCardReward>? partyCards)
    {
        for (var column = 0; column < WireCardColumnCount; column++)
        {
            var member = partyCards?.SingleOrDefault(card => card.Seat == column);
            if (partyCards is null ? column != RewardColumn : member is null)
            {
                // A column with nothing on it still declares its gold entry, which is none. This
                // is the reference server's own shape for the seven columns it does not use.
                writer.Write((byte)1);
                writer.Write(0);
                writer.Write(0);
                continue;
            }

            var reward = RewardOf(member?.Cards ?? cards);
            writer.Write((byte)(reward is null ? 1 : 2));
            writer.Write(0); // the gold entry's identifier is not read
            writer.Write(member?.Gold ?? cardGold);
            if (reward is { } piece)
            {
                writer.Write(piece.ItemId);
                writer.Write(piece.Quantity);
            }
        }
    }

    /// <summary>
    /// A row whose every column declares nothing at all.
    /// </summary>
    /// <remarks>
    /// Black-diamond eligibility and its personal reward are frozen with settlement.
    /// Empty eligibility writes no entries; an eligible reward keeps the gold placeholder.
    /// </remarks>
    private static int WriteBlackDiamondRow(BinaryWriter writer, ClearRewardCardFace? face,
        IReadOnlyList<PartyCardReward>? partyCards)
    {
        var entries = 0;
        for (var seat = 0; seat < WireCardColumnCount; seat++)
        {
            var bonus = partyCards is null ? (seat == RewardColumn ? face : null)
                : partyCards.SingleOrDefault(member => member.Seat == seat)?.BlackDiamond;
            if (bonus is null)
            {
                writer.Write((byte)0);
                continue;
            }
            var item = RewardOf(bonus.Slots);
            var count = item is null ? 1 : 2;
            entries += count;
            writer.Write((byte)count);
            writer.Write(0);
            writer.Write(bonus.Gold);
            if (item is { } piece)
            {
                writer.Write(piece.ItemId);
                writer.Write(piece.Quantity);
            }
        }
        return entries;
    }

    private static void WriteUnturnedRow(BinaryWriter writer)
    {
        for (var column = 0; column < WireCardColumnCount; column++)
            writer.Write((byte)0);
    }

    /// <summary>
    /// What this clear puts on its card, or <c>null</c> when it put nothing there.
    /// </summary>
    /// <remarks>
    /// <b>One position, not four.</b> The client never reports which card the player clicked -- the
    /// request's position byte is a written-in constant zero -- so which card is turned over cannot
    /// change what is behind it. The panel turns the clicked card over and draws the one reward the
    /// answer names. A face declaring four different things would therefore be declaring three that
    /// no player can ever reach; see <c>protocol/game/client-to-server/0x0047.md</c>.
    /// </remarks>
    private static ClearRewardCardItem? RewardOf(IReadOnlyList<ClearRewardCardSlot> cards) =>
        PositionOf(cards, RewardPosition);

    /// <summary>How many columns each row of the tail carries.</summary>
    /// <remarks>
    /// Eight, which all three of the client's parse loops bound with <c>cmp ..., 8</c>. The panel
    /// draws four of them; the frame carries eight either way, so this is a fact about the frame
    /// rather than about the panel.
    /// </remarks>
    public const int WireCardColumnCount = 8;

    /// <summary>Which column of the free row this clear's reward is declared on.</summary>
    /// <remarks>
    /// Zero, and it is the same zero the flip answer writes as the seat that turned a card over:
    /// the value the answer carries is what the panel then uses to pick whose contents a column
    /// draws. While this server answers for one player at seat zero, the reward has to be on
    /// column zero for that pick to land on it. Changing one without the other would put a card
    /// face up with nothing behind it.
    /// </remarks>
    private const int RewardColumn = 0;

    /// <summary>Which position of the settled face is the one that reaches the player.</summary>
    /// <remarks>
    /// Zero, because zero is the position the flip claims: the client's request carries a constant
    /// zero and the claim takes it at its word. The rule service still settles a whole face; the
    /// positions after this one have no way to reach a player and are not put on the wire.
    /// </remarks>
    private const int RewardPosition = 0;

    /// <summary>The width of one identifier-and-amount entry.</summary>
    private const int CardEntryLength = 8;

    /// <summary>
    /// What the frame is with no per-object experience entries and nothing behind the card.
    /// </summary>
    /// <remarks>
    /// The pre-tail block is 159 bytes, the record count one, the free row's eight columns
    /// 1 + 8 + 63, the cost four, the two empty rows eight apiece, and the trailer twenty-two.
    /// A piece behind the card adds one entry to the free row's first column and nothing else.
    /// </remarks>
    public const int ClearRewardBodyLength = 274;

    /// <summary>What the frame measures for a given face.</summary>
    public static int ClearRewardBodyLengthFor(IReadOnlyList<ClearRewardCardSlot>? cards) =>
        ClearRewardBodyLength
            + (RewardOf(cards ?? []) is null ? 0 : CardEntryLength);

    private const int FixedRewardSlotCount = 23;

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
                message.WireUserId,
                message.ClearTimeMilliseconds,
                message.ClientRankPoint,
                message.QuestMaze));
}

internal sealed class ClearDungeonRewardEncoder
    : GameNotiMessageEncoder<ClearDungeonRewardNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ClearDungeonRewardNotification message) =>
        OnePayload(
            GameProtocol.ClearDungeonRewardNotificationType,
            DungeonClearPayloadCodec.EncodeClearDungeonReward(
                message.ClearBase,
                message.RankBonus,
                message.FreeCards,
                message.CardGold,
                message.PaidFlipCost,
                message.PartyCards, message.KillExperience, message.Experience, message.BlackDiamond));
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
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    IDungeonRunDomainService runs,
    IDungeonSettlementRuleService settlementRules,
    ICardRewardDomainService cardRewards,
    ICommandBus commandBus,
    IQuestOfferRefreshDomainService offers,
    IGameMessageSender sender,
    ILogger<SetPlayResultHandler> logger,
    DungeonCardPresentationCoordinator? cardPresentation = null, SelectedSessionSafety? safety = null,
    Dfo.Server.Abstractions.DomainEvents.IEventService? eventNotices = null)
    : ClientMessageHandler<SetPlayResultRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SetPlayResultRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var capturedRecipient = session is null ? null : safety?.Capture(session) ?? new SelectedSessionRecipient(session, context.Connection);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return;
        }

        var currentRun = runs.Find(session.CharacterId);
        if (currentRun is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }

        // Both story and ordinary runs settle from the run's verified clear fact. A party
        // member's personal quest progress is not a second completion gate.
        var settlement = await settlementRules.ForAsync(
            session.AccountId,
            session.CharacterId,
            currentRun,
            cancellationToken).ConfigureAwait(false);
        if (!settlement.CanComplete)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: run purpose {Purpose} "
                    + "has not met its completion rule; ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                currentRun.Layout.Purpose);
            return;
        }

        // Claimed, not merely looked up. The client says when a run is over, and since 2026-08-29
        // saying so pays experience -- so a repeated report would be a repeated payment. The same
        // guard a kill has, for the same reason, and the run is left in progress either way: what
        // ends it is the character walking out.
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;

        var settlementClaim = runs.TryClaimSettlement(session.CharacterId, currentRun.RunId);
        if (settlementClaim is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress, or "
                    + "one already settled; ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }
        var run = settlementClaim.Run;
        var settlementCommitted = false;
        var durableCommitted = false;
        try
        {
            // The cards are fixed here, and here is the only place they can be fixed. Above is the
            // claim: a repeated result report is turned back before it reaches this line, so one run
            // draws one pair of faces however many times its result is reported. Below is every frame
            // this settlement sends, and the reward breakdown is one of them -- so a face fixed after
            // them would be reported as the zeroes it was before it existed.
            //
            // The level handed in is the one the character cleared at, taken before the award below
            // can move it: what band a clear's cards are drawn from is a fact about the run that was
            // just fought, not about the level the last of its experience happened to buy.
            var clearProgress = await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
            if (clearProgress is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                return;
            }
            var settled = await cardRewards.SettleAsync(
                new ClearRewardSettlement(
                    session.CharacterId,
                    clearProgress.Level,
                    run.PartyMemberCount,
                    run.RunId),
                cancellationToken).ConfigureAwait(false);
            if (settled is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                return;
            }
            // Every client needs the same reward row indexed by party seat. Fixing the
            // other faces is idempotent and does not grant their experience or inventory.
            List<PartyCardReward>? partyCards = null;
            if (run.Participants.Count > 1)
            {
                partyCards = [];
                foreach (var participant in run.Participants.OrderBy(value => value.Slot))
                {
                    if (runs.Find(participant.CharacterId)?.RunId != run.RunId) continue;
                    var participantProgress = await characters.Get(participant.CharacterId)
                        .ProgressAsync(cancellationToken).ConfigureAwait(false);
                    if (participantProgress is null) continue;
                    var memberCards = participant.CharacterId == session.CharacterId ? settled
                        : await cardRewards.SettleAsync(new ClearRewardSettlement(
                            participant.CharacterId, participantProgress.Level, run.PartyMemberCount,
                            run.RunId), cancellationToken).ConfigureAwait(false);
                    if (memberCards is not null)
                        partyCards.Add(new(participant.Slot, memberCards.Cards.Free.Slots,
                            memberCards.Cards.Free.Gold) { BlackDiamond = memberCards.Cards.BlackDiamond });
                }
            }
            var freeCards = settled.Cards.Free.Slots;
            var cardGold = settled.Cards.Free.Gold;

            // What the result screen is told a paid flip costs. The settlement answers the archive's
            // price for the level this clear settled at, and answers nothing for a level the archive
            // prices none at -- which is not the same as pricing it at nothing, so the two are not
            // written the same way. Neither value is invented here: both are the frame's own, named
            // and argued where the frame is built.
            var paidFlipCost =
                settled.PaidFlipCost ?? DungeonClearPayloadCodec.UnpricedPaidFlipCost;

            var elapsed = DateTimeOffset.UtcNow - run.StartedAt;
            var clearTime = (int)Math.Clamp(elapsed.TotalMilliseconds, 0, int.MaxValue);

            // Asked before anything is paid, and the same refusal a kill makes. The experience frame
            // carries the character's remaining skill points as well as the total, so without them
            // there is no frame to report the new standing on -- and paying anyway would leave the
            // character credited, the run spent, and the client still drawing the total it entered
            // with.
            //
            // Asked of the character rather than read off the session, for the reason a kill asks:
            // the session's copy is entry's projection and nothing recomputes it, so a character who
            // levelled during the run would be told the figure they walked in with.
            //
            // The settlement claim remains retryable until the award commits. Missing this mirror
            // therefore leaves the run unpaid rather than spending its one settlement attempt.
            var character = characters.Get(session.CharacterId);
            var mirror = await character.RemainingSkillPointsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (mirror is null)
            {
                runs.ReleaseSettlement(settlementClaim);
                logger.LogWarning(
                    "Set play result {ConnectionId} character={CharacterId}: the character has no "
                        + "skill point ledger to report, so the clear was neither paid nor reported",
                    context.Connection.ConnectionId,
                    session.CharacterId.Value);
                await sender.SendExplicitAsync(
                    context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery(
                        [
                            new PlayResultNotification(
                                session.WireUserId,
                                clearTime,
                                message.ClientRankPoint,
                                settlement.QuestMaze),
                            new ClearDungeonRewardNotification(
                                0, 0, freeCards, cardGold, paidFlipCost, partyCards, BlackDiamond: settled.Cards.BlackDiamond),
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
                        message.ClientRankPoint, run.KillSummary.Total, run.PartyMemberCount), Session: session,
                    RunId:run.RunId.Value,SettlementLevel:clearProgress.Level),
                cancellationToken).ConfigureAwait(false));

            durableCommitted = true;

            // The whole award, in its parts. Experience is a number nobody can check by looking, so
            // this line is what makes a live session usable as evidence for what a clear paid -- and
            // what the result screen's own figures have to agree with.
            logger.LogInformation(
                "Set play result {ConnectionId} character={CharacterId}: dungeon {DungeonId} cleared "
                    + "in {ClearTime}ms at rank {Rank}; clearBase={ClearBase} rankBonus={RankBonus} "
                    + "award={Award} kills={Kills} party={PartyCount} killExperience={KillExperience}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                run.Layout.DungeonId,
                clearTime,
                message.ClientRankPoint,
                paid.ClearBase,
                paid.RankBonus,
                paid.Award, run.KillSummary.Total, run.PartyMemberCount, run.KillExperience);

            // The award is the result of this write. When none was made, the character's current
            // progress supplies the frame; the session remains only a compatibility projection.
            var award = paid.Award;
            var currentProgress = award is null
                ? await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false)
                : new CharacterProgressionState(award.Level, award.Total);
            if (currentProgress is null)
                throw new InvalidOperationException("The current progress disappeared after its durable award.");

            if (runs.CommitSettlement(settlementClaim) is null)
            {
                throw new InvalidOperationException(
                    "The dungeon settlement claim was no longer current after its durable award.");
            }
            settlementCommitted = true;
            if (paid.AttendanceChanged && eventNotices is not null)
                await eventNotices.ProjectAsync(new Dfo.Server.Abstractions.Notifications.NotificationAudience.TheseSessions([session]),
                    new Dfo.Server.Abstractions.Events.EventsChanged(),cancellationToken);
            var level = currentProgress.Level;
            var total = currentProgress.Experience;
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
                    // And the same second ask a kill makes: crossing a level grants more points, so
                    // the figure taken before the award is a level out of date. Only a level moves
                    // that ledger here -- a clear learns no skills and stores no bonus.
                    mirror = await character.RemainingSkillPointsAsync(cancellationToken)
                        .ConfigureAwait(false) ?? mirror;

                    result = await offers
                        .RefreshAsync(result, session.AccountId, session.CharacterId, level,
                            cancellationToken).ConfigureAwait(false);
                }

            }

            List<IGameServerMessage> frames =
            [
                new PlayResultNotification(
                    session.WireUserId,
                    clearTime,
                    message.ClientRankPoint,
                    settlement.QuestMaze),
                new ExperienceNotification(
                    level,
                    award?.NotificationTotal ?? total,
                    mirror,
                    AccountHonorLevel.Initial,
                    ExperienceBonusLines.Of(award)),
            ];

            if (paid.DifficultyUnlocked is { } unlocked)
                frames.Add(new DungeonPermissionStateMessage(
                    [new CharacterDungeonProgressState(unlocked.DungeonId, (byte)unlocked.Maximum)]));

            if (award?.LeveledUp == true)
            {
                frames.Add(new AvailableQuestListMessage(
                    level, result.World.Content.AvailableQuests));
            }

            // Render the earned reward, independent of any progression ceiling.
            frames.Add(award is null
                ? new ClearDungeonRewardNotification(0, 0, freeCards, cardGold, paidFlipCost, partyCards, BlackDiamond: settled.Cards.BlackDiamond)
                : new ClearDungeonRewardNotification(
                    award.Breakdown.Base, award.Breakdown.ClearRank, freeCards, cardGold, paidFlipCost, partyCards, run.KillExperience, award.Breakdown, settled.Cards.BlackDiamond));

            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(frames, []),
                cancellationToken).ConfigureAwait(false);
            cardPresentation?.ScheduleAfterSettlement(session, currentRun.RunId);
        }
        catch (Exception failure)
        {
            if (durableCommitted && !settlementCommitted)
                throw safety is not null
                    ? await safety.PublicationFailedAsync(capturedRecipient!, failure, nameof(SetPlayResultHandler)).ConfigureAwait(false)
                    : new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                        Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.PostCommitPublicationFailure,
                        "Durable clear effects could not be published.", failure);
            if (!settlementCommitted && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
                runs.ReleaseSettlement(settlementClaim);
            throw;
        }
    }
}
