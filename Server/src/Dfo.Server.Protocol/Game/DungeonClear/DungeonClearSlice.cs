using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Quests;
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
internal sealed record ClearDungeonRewardNotification(uint ClearBase, uint RankBonus)
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

    /// <summary>The merchant a working A21 session puts on the result screen.</summary>
    public const int SecretShopNpcId = 1000;

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
    /// Two of its figures now carry what the clear actually paid; the rest are still zero, and
    /// zero is still the honest value for a reward this server does not grant. A clear's avatar,
    /// creature, adventure-group and premium bonuses each need a source this server has not got,
    /// and a frame carrying invented figures would put numbers on the result screen that nothing
    /// else could account for.
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
    public static byte[] EncodeClearDungeonReward(uint clearBase, uint rankBonus)
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

        // The card tail. One free-card seat because no item is behind it, then seven seats, then
        // what a paid flip would cost.
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write(0); // free-card item
        writer.Write(0); // free-card gold
        for (var seat = 0; seat < 7; seat++)
        {
            writer.Write((byte)1);
            writer.Write(0);
            writer.Write(0);
        }

        writer.Write(0); // paid-card cost
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
                message.ClearBase, message.RankBonus));
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
    ICommandBus commandBus,
    QuestOfferRefresher offers,
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

        var run = runs.Find(session.CharacterId);
        if (run is null)
        {
            logger.LogWarning(
                "Set play result {ConnectionId} character={CharacterId}: no run in progress, "
                    + "ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return;
        }

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
                        new ClearDungeonRewardNotification(0, 0),
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

        // Declared only where it was granted. The command's two figures are what the clear was
        // worth; when the award did not happen -- no such character, or a row that would not take
        // the write -- the character received none of it, and putting those figures on the result
        // screen beside a total that did not move would draw a breakdown of experience nobody has.
        frames.Add(award is null
            ? new ClearDungeonRewardNotification(0, 0)
            : new ClearDungeonRewardNotification(paid.ClearBase, paid.RankBonus));

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }
}
