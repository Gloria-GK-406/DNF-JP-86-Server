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

/// <summary>NOTI 0x0025 <c>EXP</c>: what the character is worth after the kill.</summary>
/// <param name="SeriaBlessing">
/// How much of the gain the channel added. Declared rather than folded into the total, because the
/// client subtracts every named line from the gain to work out what to draw as the base -- so an
/// amount paid but not declared here is drawn as though the kill itself were worth that much more.
/// </param>
internal sealed record ExperienceNotification(
    byte Level,
    uint TotalExperience,
    CharacterSkillProtocolMirrorState SkillPoints,
    AccountHonorLevel Honor,
    uint SeriaBlessing = 0,
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
    /// The experience frame, in the fixed A21 layout.
    /// </summary>
    /// <remarks>
    /// Eighty-three bytes whose offsets are the reference builder's. Only four carry anything
    /// today: the level, the total experience, the skill points, and the account's honour
    /// standing. The bonus lines -- party, fatigue, Seria's blessing, the growth contract, the
    /// internet cafe, the elite kill, the growth capsule, the channel and guild bonuses -- are
    /// each a separate line in the client's experience popup, and every one of them is a reward
    /// rule this server does not have. Zero is the honest value for a bonus that was not awarded.
    ///
    /// The level and total are what the kill left behind, and until 2026-08-28 they were not: this
    /// frame reported what the character already had, because nothing granted anything. The
    /// channel's line is now paid and declared; the rest are still zero, and zero remains the
    /// honest value for a bonus this server does not award.
    /// <para>
    /// The lines are not decoration. The client reads the gain as this total minus the one it
    /// holds, then subtracts every line below to decide what the base was worth
    /// (<c>0x01179476</c> onwards). A line paid but left at zero is therefore drawn as part of the
    /// base, and a line declared but not paid would make the base read short by that much.
    /// </para>
    /// </remarks>
    public static byte[] EncodeExperience(
        byte level,
        uint totalExperience,
        CharacterSkillProtocolMirrorState skillPoints,
        AccountHonorLevel honor,
        uint seriaBlessing,
        bool labelProbe = false)
    {
        var body = new byte[ExperienceBodyLength];
        var writer = new SpanWriter(body);
        // A measurement, off in play: every bonus slot gets a distinct amount so
        // the client names each line itself. See ExperienceDiagnostics -- the one
        // slot whose name this server knows was learned exactly this way, and it
        // was not the name the reference server gives it.
        var p = labelProbe ? ExperienceDiagnostics.ProbeAmounts : null;
        uint Slot(int index, uint value) => p is null ? value : p[index];
        writer.Byte(level); // +0x00
        writer.U32(totalExperience); // +0x01
        writer.U32(Slot(0, 0)); // +0x05 party experience
        writer.U16(skillPoints.Page0Sp); // +0x09
        writer.U16(skillPoints.Page1Sp); // +0x0B
        writer.U16(skillPoints.Page0Tp); // +0x0D
        writer.U16(skillPoints.Page1Tp); // +0x0F
        writer.U32(0); // +0x11 PvP victory points
        writer.U32(Slot(1, 0)); // +0x15 fatigue buff experience
        writer.Byte(0); // +0x19 presentation mode
        writer.U32(Slot(2, seriaBlessing)); // +0x1A
        writer.U32(Slot(3, 0)); // +0x1E growth contract
        writer.U32(Slot(4, 0)); // +0x22 unnamed
        writer.U32(Slot(5, 0)); // +0x26 fatigue burn
        writer.U32(Slot(6, 0)); // +0x2A internet cafe
        writer.Byte(0); // +0x2E itemised entry count
        writer.U32(Slot(7, 0)); // +0x2F elite monster kill
        writer.U32(Slot(8, 0)); // +0x33 unnamed
        writer.U32(Slot(9, 0)); // +0x37 growth capsule
        writer.U32(honor.Level); // +0x3B
        writer.U32(honor.Experience); // +0x3F
        writer.U32(Slot(10, 0)); // +0x43 speed-growth buff
        writer.U32(Slot(11, 0)); // +0x47 unnamed
        writer.U32(Slot(12, 0)); // +0x4B
        writer.U32(Slot(13, 0)); // +0x4F guild bonus
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
                message.SeriaBlessing, message.LabelProbe));
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
                AmountOf(award, ExperienceBonusKind.SeriaBlessing), diagnostics.LabelProbe),
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

    /// <summary>How much of an award came from one named line of the frame.</summary>
    /// <remarks>
    /// Summed rather than taken singly: nothing stops two reasons sharing a line, and a
    /// first-match read would drop the second silently -- the kind of loss that shows up only as a
    /// breakdown the player can see does not add up.
    /// </remarks>
    private static uint AmountOf(ExperienceAward? award, ExperienceBonusKind kind)
    {
        if (award is null)
            return 0;

        var total = 0UL;
        foreach (var contribution in award.Contributions)
        {
            if (contribution.Kind == kind)
                total += contribution.Amount;
        }

        return total > uint.MaxValue ? uint.MaxValue : (uint)total;
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
