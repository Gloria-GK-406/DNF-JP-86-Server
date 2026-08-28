using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.Quests;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

/// <summary>
/// CMD 0x0027 <c>DIE_MONSTER</c>: the client reporting that a room actor has died.
/// </summary>
/// <remarks>
/// Combat is the client's to decide. It reports the swing with CMD 0x0026 <c>USE_SKILL</c>, which
/// the server does not answer at all, and then reports the death here. The server's part is to
/// acknowledge it and to say what the kill was worth -- not to adjudicate it.
///
/// The body is a hundred-odd bytes of attack detail. Only the first four are read: the actor's
/// running number, which is the one this server handed out in <c>NOTI 0x001D START_MAP</c>, and
/// the user that killed it. The rest describes how it died, which nothing here acts on.
/// </remarks>
public sealed record MonsterDeathRequest(
    GameMessageMetadata Metadata,
    ushort ActorSequence,
    ushort WireUserId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMonsterDeath;
}

/// <summary>
/// How much of a gain each of the notice's named lines accounts for.
/// </summary>
/// <remarks>
/// Declared rather than merely paid, because the client works out the base line by subtracting
/// every named line from the gain -- so an amount paid into the total and left at zero here is
/// drawn as though the occasion itself were worth that much more.
/// <para>
/// Gathered by line rather than taken singly: nothing stops two reasons sharing a line, and a
/// first-match read would drop the second silently -- the kind of loss that shows up only as a
/// breakdown the player can see does not add up.
/// </para>
/// </remarks>
internal sealed class ExperienceBonusLines
{
    /// <summary>One place per kind, so a line is addressed by name rather than by offset.</summary>
    private static readonly int KindCount = Enum.GetValues<ExperienceBonusKind>().Length;

    private readonly uint[] _amounts;

    private ExperienceBonusLines(uint[] amounts) => this._amounts = amounts;

    /// <summary>An occasion that declared nothing.</summary>
    public static ExperienceBonusLines None { get; } = new(new uint[KindCount]);

    /// <summary>What each of an award's contributions declared, gathered by line.</summary>
    /// <remarks>
    /// A contribution of no identified line is skipped rather than dropped: it stays in the award's
    /// total, and the client draws it as part of the base. That is the honest reading -- the amount
    /// is right and only its attribution is coarse.
    /// </remarks>
    public static ExperienceBonusLines Of(ExperienceAward? award)
    {
        if (award is null || award.Contributions.Count == 0)
            return None;

        var amounts = new uint[KindCount];
        foreach (var contribution in award.Contributions)
        {
            if (contribution.Kind is ExperienceBonusKind.Unlisted)
                continue;

            // Saturating: the frame's field is a u32, and a sum that wrapped would draw a line
            // smaller than the amount it stands for -- which is exactly the breakdown error this
            // whole type exists to prevent.
            var total = (ulong)amounts[(int)contribution.Kind] + contribution.Amount;
            amounts[(int)contribution.Kind] = total > uint.MaxValue ? uint.MaxValue : (uint)total;
        }

        return new ExperienceBonusLines(amounts);
    }

    /// <summary>What this line accounts for.</summary>
    public uint this[ExperienceBonusKind kind] =>
        kind is ExperienceBonusKind.Unlisted ? 0 : this._amounts[(int)kind];
}

/// <summary>NOTI 0x0025 <c>EXP</c>: what the character is worth after the kill.</summary>
/// <param name="Lines">How much of the gain each named line accounts for.</param>
internal sealed record ExperienceNotification(
    byte Level,
    uint TotalExperience,
    CharacterSkillProtocolMirrorState SkillPoints,
    AccountHonorLevel Honor,
    ExperienceBonusLines Lines,
    bool LabelProbe = false) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExperience;
}

/// <summary>NOTI 0x0026 <c>DIE_MONSTER</c>: the death acknowledged, and what it dropped.</summary>
internal sealed record MonsterDeathNotification(ushort ActorSequence) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMonsterDeath;
}

internal static class MonsterDeathPayloadCodec
{
    /// <summary>The actor number and the killing user; everything after is attack detail.</summary>
    public const int MinimumBodyLength = 4;

    /// <summary>The length the client reads when no experience line is itemised.</summary>
    public const int ExperienceBodyLength = 83;

    /// <summary>
    /// The kill acknowledged: the actor's number, its drops, and four closing bytes.
    /// </summary>
    /// <remarks>
    /// Sent with no drops. What a monster drops is content this server does not model yet, and an
    /// invented drop would put an item on the floor that no later frame can account for. A working
    /// A21 session's empty-handed kill is these same seven bytes.
    ///
    /// A drop, when there are any, is forty-eight bytes: <c>u16 sceneSlot, u32 templateId,
    /// u8 upgrade, u32 count, 5 zero, u32 dropGroup, 24 zero, u16 owner, u16 zero</c>.
    /// </remarks>
    public static byte[] EncodeMonsterDeath(ushort actorSequence)
    {
        using var stream = new MemoryStream(7);
        using var writer = new BinaryWriter(stream);
        writer.Write(actorSequence);
        writer.Write((byte)0); // no drops
        writer.Write(MonsterDeathTail);
        return stream.ToArray();
    }

    /// <summary>The four bytes every A21 kill acknowledgement closes with.</summary>
    private static readonly byte[] MonsterDeathTail = [0x00, 0x00, 0xFF, 0x00];

    /// <summary>
    /// One line of the notice: which bonus it is, where the client reads it, and what it calls it.
    /// </summary>
    /// <param name="ClientName">
    /// The name the client itself draws over this line. Kept beside the offset rather than left in
    /// a document, because the pair is what a later reader has to check and the offset alone cannot
    /// be checked at all -- every offset looks equally plausible.
    /// </param>
    public sealed record ExperienceNoticeLine(
        ExperienceBonusKind Kind, int Offset, string ClientName);

    /// <summary>
    /// Where the client draws each named line from, and under what name.
    /// </summary>
    /// <remarks>
    /// Measured, not transcribed. A probe paid a distinct amount into all fourteen bonus slots and
    /// the client labelled every line itself; the two rounds and their cross-validation are in
    /// <c>Docs/ExperienceNoticeSlots.md</c>. Nine slots are both drawn and subtracted from the
    /// base, and those nine are here.
    /// <para>
    /// <c>+0x2F</c> and <c>+0x33</c> are absent on purpose: the first is drawn but not subtracted
    /// and the second subtracted but not drawn, so either one leaves the player looking at a
    /// breakdown that does not add up. <c>+0x22</c>, <c>+0x37</c> and <c>+0x47</c> are absent
    /// because the client neither draws nor subtracts them.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ExperienceNoticeLine> NamedLines { get; } =
    [
        new(ExperienceBonusKind.PartyReward, 0x05, "队伍奖励"),
        new(ExperienceBonusKind.FatigueBuff, 0x15, "疲劳值Buff"),
        new(ExperienceBonusKind.SeriaBlessing, 0x1A, "赛丽亚的祝福奖励"),
        new(ExperienceBonusKind.GrowthContract, 0x1E, "成长之契约奖励"),
        new(ExperienceBonusKind.FatigueBurn, 0x26, "疲劳值燃烧奖励"),
        new(ExperienceBonusKind.InternetCafe, 0x2A, "网吧奖励"),
        new(ExperienceBonusKind.RapidGrowthBuff, 0x43, "极速成长Buff奖励"),
        new(ExperienceBonusKind.SeriaWelcome, 0x4B, "赛丽亚的欢迎"),
        new(ExperienceBonusKind.GuildReward, 0x4F, "公会奖励"),
    ];

    /// <summary>
    /// The experience frame, in the fixed A21 layout.
    /// </summary>
    /// <remarks>
    /// Eighty-three bytes whose offsets are the reference builder's. The level, the total
    /// experience, the skill points and the account's honour standing carry the character's
    /// standing; the nine named bonus lines carry what was paid on top of the occasion, each on
    /// the line the client draws it as.
    ///
    /// The level and total are what the occasion left behind, and until 2026-08-28 they were not:
    /// this frame reported what the character already had, because nothing granted anything.
    /// <para>
    /// The lines are not decoration. The client reads the gain as this total minus the one it
    /// holds, then subtracts every line below to decide what the base was worth
    /// (<c>0x01179476</c> onwards). A line paid but left at zero is therefore drawn as part of the
    /// base, and a line declared but not paid would make the base read short by that much.
    /// </para>
    /// <para>
    /// Five slots stay at zero whatever is paid. Two of them -- <c>+0x2F</c> and <c>+0x33</c> --
    /// are the ones the client itself handles asymmetrically, and writing to either is how this
    /// server would put a breakdown on screen that does not add up. The reference server has names
    /// for them; the names are what makes them look fillable.
    /// </para>
    /// </remarks>
    public static byte[] EncodeExperience(
        byte level,
        uint totalExperience,
        CharacterSkillProtocolMirrorState skillPoints,
        AccountHonorLevel honor,
        ExperienceBonusLines lines,
        bool labelProbe = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var body = new byte[ExperienceBodyLength];
        var writer = new SpanWriter(body);
        // A measurement, off in play: every bonus slot gets a distinct amount so
        // the client names each line itself. See ExperienceDiagnostics -- every
        // name in NamedLines was learned exactly this way, and two of them
        // were not the names the reference server gives those slots.
        var p = labelProbe ? ExperienceDiagnostics.ProbeAmounts : null;
        uint Slot(int index, uint value) => p is null ? value : p[index];
        uint Line(int index, ExperienceBonusKind kind) => Slot(index, lines[kind]);
        writer.Byte(level); // +0x00
        writer.U32(totalExperience); // +0x01
        writer.U32(Line(0, ExperienceBonusKind.PartyReward)); // +0x05 队伍奖励
        writer.U16(skillPoints.Page0Sp); // +0x09
        writer.U16(skillPoints.Page1Sp); // +0x0B
        writer.U16(skillPoints.Page0Tp); // +0x0D
        writer.U16(skillPoints.Page1Tp); // +0x0F
        writer.U32(0); // +0x11 PvP victory points
        writer.U32(Line(1, ExperienceBonusKind.FatigueBuff)); // +0x15 疲劳值Buff
        writer.Byte(0); // +0x19 presentation mode
        writer.U32(Line(2, ExperienceBonusKind.SeriaBlessing)); // +0x1A 赛丽亚的祝福奖励
        writer.U32(Line(3, ExperienceBonusKind.GrowthContract)); // +0x1E 成长之契约奖励
        writer.U32(Slot(4, 0)); // +0x22 neither drawn nor subtracted
        writer.U32(Line(5, ExperienceBonusKind.FatigueBurn)); // +0x26 疲劳值燃烧奖励
        writer.U32(Line(6, ExperienceBonusKind.InternetCafe)); // +0x2A 网吧奖励
        writer.Byte(0); // +0x2E itemised entry count
        writer.U32(Slot(7, 0)); // +0x2F drawn but NOT subtracted -- unusable
        writer.U32(Slot(8, 0)); // +0x33 subtracted but NOT drawn -- unusable
        writer.U32(Slot(9, 0)); // +0x37 neither drawn nor subtracted
        writer.U32(honor.Level); // +0x3B
        writer.U32(honor.Experience); // +0x3F
        writer.U32(Line(10, ExperienceBonusKind.RapidGrowthBuff)); // +0x43 极速成长Buff奖励
        writer.U32(Slot(11, 0)); // +0x47 neither drawn nor subtracted
        writer.U32(Line(12, ExperienceBonusKind.SeriaWelcome)); // +0x4B 赛丽亚的欢迎
        writer.U32(Line(13, ExperienceBonusKind.GuildReward)); // +0x4F 公会奖励
        return body;
    }

    /// <summary>A cursor over a fixed-length body, so the offsets above stay readable.</summary>
    private ref struct SpanWriter(Span<byte> target)
    {
        private readonly Span<byte> _target = target;
        private int _at = 0;

        public void Byte(byte value) => _target[_at++] = value;

        public void U16(ushort value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(_target[_at..], value);
            _at += 2;
        }

        public void U32(uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_target[_at..], value);
            _at += 4;
        }
    }
}

internal sealed class ExperienceEncoder : GameNotiMessageEncoder<ExperienceNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ExperienceNotification message) =>
        OnePayload(
            GameProtocol.ExperienceNotificationType,
            MonsterDeathPayloadCodec.EncodeExperience(
                message.Level, message.TotalExperience, message.SkillPoints, message.Honor,
                message.Lines, message.LabelProbe));
}

internal sealed class MonsterDeathEncoder : GameNotiMessageEncoder<MonsterDeathNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        MonsterDeathNotification message) =>
        OnePayload(
            GameProtocol.MonsterDeathNotificationType,
            MonsterDeathPayloadCodec.EncodeMonsterDeath(message.ActorSequence));
}

internal sealed class MonsterDeathDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MonsterDeathPayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"DIE_MONSTER requires at least four body bytes; received '{body.Length}'.");
        }

        return new MonsterDeathRequest(
            metadata,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
    }
}

/// <summary>Acknowledges a kill and reports what the character is worth after it.</summary>
public sealed class MonsterDeathHandler(
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    IDungeonRunDomainService runs,
    IDungeonRoomActorSource roomActors,
    ICommandBus commandBus,
    QuestOfferRefresher offers,
    IGameMessageSender sender,
    ExperienceDiagnostics diagnostics,
    ILogger<MonsterDeathHandler> logger) : ClientMessageHandler<MonsterDeathRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MonsterDeathRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !runtimeSessions.TryGet(session, out var result, out _)
            || result is null)
        {
            logger.LogWarning(
                "Monster death {ConnectionId}: no published session, ignoring actor={Actor}",
                context.Connection.ConnectionId,
                message.ActorSequence);
            return;
        }

        // Asked before anything is consumed. The experience frame carries the skill-point mirror
        // as well as the total, so without one there is no frame to send -- and awarding first
        // would leave the character paid, the death spent, and the session still holding the old
        // total with no frame to correct it.
        var mirror = result.CharacterEntry.Skills.ProtocolMirror;
        if (mirror is null)
        {
            logger.LogWarning(
                "Monster death {ConnectionId}: the session carries no skill point mirror, "
                    + "acknowledging the kill without awarding or reporting experience",
                context.Connection.ConnectionId);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new MonsterDeathNotification(message.ActorSequence)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Reported dead first, and everything else hangs off whether that took. The client repeats
        // a death it was not answered for, and the run refuses the repeat -- so this is the one
        // place that can tell a kill from its echo. Awarding before it would pay for both.
        var run = runs.TryReportActorDead(session.CharacterId, message.ActorSequence);
        var award = run is null
            ? null
            : await this.AwardAsync(context, session, run, message.ActorSequence, cancellationToken)
                .ConfigureAwait(false);

        logger.LogInformation(
            "Monster death {ConnectionId} user={WireUserId}: actor={Actor} killedBy={KilledBy}",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.ActorSequence,
            message.WireUserId);

        // The session is corrected before the frames go out. Every later frame reads the character
        // from here -- including the next kill's own experience frame -- so a session left holding
        // the entry-time total would go on reporting it for the rest of the run.
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

            // A level decides which quests are offered, so crossing one has to be followed by
            // working that out again -- otherwise a quest whose band the character has just
            // entered stays hidden until they next enter the world.
            if (award.LeveledUp)
            {
                result = await offers
                    .RefreshAsync(result, session.AccountId, session.CharacterId, level,
                        cancellationToken).ConfigureAwait(false);
            }

            runtimeSessions.ReplaceExact(session, result);
        }

        // The experience frame first, then the acknowledgement. That is the order a working A21
        // session sends them in, and the popup the first one raises names the kill the second one
        // confirms.
        List<IGameServerMessage> frames =
        [
            new ExperienceNotification(
                // The probe raises only what is reported, never what is stored: the
                // client draws a breakdown of a gain nobody was paid, and the next
                // kill's frame carries the real total and puts it back. A measurement
                // that moved the character would have to be undone by hand, and the
                // account it ran on could not be trusted for anything afterwards.
                level,
                diagnostics.LabelProbe ? total + ExperienceDiagnostics.ProbeTotal : total,
                mirror, AccountHonorLevel.Initial,
                ExperienceBonusLines.Of(award), diagnostics.LabelProbe),
            new MonsterDeathNotification(message.ActorSequence),
        ];

        if (award?.LeveledUp == true)
        {
            frames.Add(new AvailableQuestListMessage(
                level, result.World.Content.AvailableQuests));
        }

        if (run is not null)
        {
            frames.AddRange(
                await this.ClearedAsync(context, session.CharacterId, run, cancellationToken)
                    .ConfigureAwait(false));
        }

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What the kill was worth, awarded, or nothing when the room cannot name what died.
    /// </summary>
    /// <remarks>
    /// The monster is looked up in the room the client is standing in, by the running number that
    /// room's actors were given. A sequence the room does not account for is not awarded: it means
    /// the client and this server disagree about what is in the room, and paying an invented
    /// monster's worth would turn that disagreement into experience.
    /// </remarks>
    private async ValueTask<ExperienceAward?> AwardAsync(
        NetworkDispatchContext context,
        SelectedSessionIdentity session,
        DungeonRunState run,
        ushort actorSequence,
        CancellationToken cancellationToken)
    {
        var declared = await roomActors.GetAsync(run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        var index = actorSequence - DungeonEntryPayloadCodec.FirstActorSequence(run);
        if (index < 0 || index >= declared.Count)
        {
            logger.LogWarning(
                "Kill experience {ConnectionId}: actor {Actor} is not one of the {Count} this room "
                    + "declares, so nothing was awarded",
                context.Connection.ConnectionId, actorSequence, declared.Count);
            return null;
        }

        var actor = declared[index];

        if (!MonsterExperienceRules.IsMonster(actor.ActorType))
        {
            logger.LogInformation(
                "Kill experience {ConnectionId}: actor {Actor} is an AI character, not a monster; "
                    + "nothing was awarded",
                context.Connection.ConnectionId, actorSequence);
            return null;
        }

        // The level the client was told, which is the dungeon's basis level wherever the map
        // declined to set one -- the same substitution the room frame made.
        var monster = new KilledMonster(
            actor.Code, run.Layout.LevelOf(actor.Level), (MonsterRank)actor.ActorType);

        var award = (await commandBus.SendAsync(
            new GrantKillExperienceCommand(
                session.AccountId,
                session.CharacterId,
                monster,
                run.Layout.DungeonId,
                run.Difficulty,
                run.Layout.ExperienceWeight),
            cancellationToken).ConfigureAwait(false)).Award;

        // The whole award, in its parts. Experience is a number nobody can check by looking, so
        // this line is what makes a live session usable as evidence for what was paid -- and it is
        // how the reference server's own arithmetic was recovered in the first place.
        logger.LogInformation(
            "Kill experience {ConnectionId} character={CharacterId}: monster={Monster} "
                + "level={MonsterLevel} rank={Rank} award={Award}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            monster.Code,
            monster.Level,
            monster.Rank,
            award);
        return award;
    }

    /// <summary>
    /// The frames that follow the kill that empties the boss room, and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// This is where a run ends. The client asks for nothing after the last monster falls until it
    /// has been told the dungeon may be cleared -- which is what a live session showed on
    /// 2026-08-26, the player left standing in an emptied boss room with only heartbeats on the
    /// wire.
    /// <para>
    /// Fired once, because the report that empties the room can only happen once: the caller has
    /// already refused a repeated death, and a run reached here is one that took a fresh one.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyList<IGameServerMessage>> ClearedAsync(
        NetworkDispatchContext context,
        CharacterId characterId,
        DungeonRunState run,
        CancellationToken cancellationToken)
    {
        if (!run.IsBossRoom)
            return [];

        var declared = await roomActors.GetAsync(run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        if (!run.IsCleared(declared, DungeonEntryPayloadCodec.FirstActorSequence(run)))
            return [];

        logger.LogInformation(
            "Monster death {ConnectionId} character={CharacterId}: boss room {RoomId} of dungeon "
                + "{DungeonId} is clear, opening the exit",
            context.Connection.ConnectionId,
            characterId.Value,
            run.CurrentRoomMapId,
            run.Layout.DungeonId);

        return
        [
            new EnableClearDungeonNotification(),
            new SecretShopNpcNotification(DungeonClearPayloadCodec.SecretShopNpcId),
        ];
    }
}
