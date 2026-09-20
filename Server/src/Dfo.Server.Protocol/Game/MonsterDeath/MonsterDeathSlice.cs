using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
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
/// <param name="Owner">
/// Which user the pieces belong to, as its wire identifier. Every captured record carries the
/// killer's own, which is what a party will one day make interesting.
/// </param>
internal sealed record MonsterDeathNotification(
    ushort ActorSequence,
    IReadOnlyList<MonsterDrop> Drops,
    ushort Owner,
    uint Instance = 0,
    IReadOnlyDictionary<ushort, ushort>? DropOwners = null) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMonsterDeath;

    /// <summary>An acknowledgement with nothing on the floor after it.</summary>
    public static MonsterDeathNotification Empty(ushort actorSequence) =>
        new(actorSequence, [], 0);
}

internal static class MonsterDeathPayloadCodec
{
    /// <summary>The actor number and the killing user; everything after is attack detail.</summary>
    public const int MinimumBodyLength = 4;

    /// <summary>The length the client reads when no experience line is itemised.</summary>
    public const int ExperienceBodyLength = 83;

    /// <summary>How many bytes one piece on the floor takes.</summary>
    public const int DropRecordLength = 48;

    /// <summary>
    /// The kill acknowledged: the actor's number, its drops, and four closing bytes.
    /// </summary>
    /// <remarks>
    /// <b>Measured, not transcribed.</b> Six frames across two reference captures carry drops --
    /// five of one piece and two of two -- and all six divide exactly as
    /// <c>u16 actor + u8 count + count x 48 + 4</c> with every byte that should be zero at zero.
    /// A 44- or 52-byte record cannot parse the two-piece frames at all.
    ///
    /// One piece is <c>u16 number, u32 itemId, u8 upgrade, u32 count, 5 zero, u32 instance,
    /// 24 zero, u16 owner, u16 zero</c>.
    /// <para>
    /// <b>Gold is item zero</b>, and its count field carries the amount rather than a number of
    /// pieces. The captures show 38, 44 and 131 paid that way, and the client reads a piece as
    /// gold by exactly that test.
    /// </para>
    /// <para>
    /// The instance is the same for every piece of one kill. The reference server writes the
    /// current Unix time there; this server writes a value derived from the run, because a run
    /// re-described has to name the same piece the same way and a clock cannot.
    /// </para>
    /// </remarks>
    public static byte[] EncodeMonsterDeath(MonsterDeathNotification message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var drops = message.Drops;
        var body = new byte[3 + (DropRecordLength * drops.Count) + MonsterDeathTail.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body, message.ActorSequence);
        body[2] = checked((byte)drops.Count);

        for (var index = 0; index < drops.Count; index++)
        {
            var record = body.AsSpan(3 + (DropRecordLength * index), DropRecordLength);
            var drop = drops[index];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record, drop.Number);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[2..], drop.ItemId);
            record[6] = 0; // upgrade level
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record[7..], drop.Count);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                record[16..], message.Instance);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                record[44..], message.DropOwners?.GetValueOrDefault(drop.Number) ?? message.Owner);
        }

        MonsterDeathTail.CopyTo(body.AsSpan(body.Length - MonsterDeathTail.Length));
        return body;
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
            MonsterDeathPayloadCodec.EncodeMonsterDeath(message));
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
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterDomainService characters,
    IDungeonRunDomainService runs,
    IMonsterKillDomainService kills,
    IDungeonClearDomainService clearEvaluator,
    DungeonClearProjectionDelivery clearProjection,
    ICommandBus commandBus,
    IQuestOfferRefreshDomainService offers,
    DungeonRunDeliveryRegistry runDeliveries,
    IGameMessageSender sender,
    ExperienceDiagnostics diagnostics,
    ILogger<MonsterDeathHandler> logger,
    DungeonPartyRecipients? partyRecipients = null) : ClientMessageHandler<MonsterDeathRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MonsterDeathRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Monster death {ConnectionId}: no published session, ignoring actor={Actor}",
                context.Connection.ConnectionId,
                message.ActorSequence);
            return;
        }

        var currentRun = runs.Find(session.CharacterId);
        if (currentRun is not null && partyRecipients is not null
            && !partyRecipients.Find(session, currentRun.RunId).Contains(session))
            return;
        if (currentRun is { PartyMemberCount: > 1 })
        {
            await this.HandlePartyDeathAsync(
                context, message, session, currentRun, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Asked before anything is consumed. The experience frame carries the character's
        // remaining skill points as well as the total, so without them there is no frame to send
        // -- and awarding first would leave the character paid, the death spent, and the session
        // still holding the old total with no frame to correct it.
        //
        // Asked of the character rather than read off the session. The session's copy is the
        // projection entry made on the way in, and nothing has ever recomputed it: a character who
        // levelled up mid-run was told the figure they entered with until they logged out again.
        var character = characters.Get(session.CharacterId);
        var mirror = await character.RemainingSkillPointsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (mirror is null)
        {
            logger.LogWarning(
                "Monster death {ConnectionId}: the character has no skill point ledger to report, "
                    + "acknowledging the kill without awarding or reporting experience",
                context.Connection.ConnectionId);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [MonsterDeathNotification.Empty(message.ActorSequence)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Claimed first, and everything else hangs off whether that took. The durable experience
        // and quest writes share one transaction. Failure releases this exact claim for one retry;
        // success commits it as dead, so an echo cannot pay either ledger twice.
        var deathClaim = runs.TryClaimActorDeath(session.CharacterId, message.ActorSequence);
        var run = deathClaim?.Run;
        ExperienceAward? award = null;
        ApplyMonsterKillResult? appliedKill = null;
        IReadOnlyList<MonsterDrop> dropped = [];
        IReadOnlyList<ushort> autoKilled = [];
        var instance = 0u;
        IdentifiedKill? killed;
        try
        {
            killed = run is null
                ? null
                : await this.KilledAsync(context, run, message.ActorSequence, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch
        {
            if (deathClaim is not null)
                runs.ReleaseActorDeath(deathClaim);
            throw;
        }
        if (run is not null && killed is { } verifiedKill)
        {
            var effectClaim = runs.TryClaimParticipantKillEffect(
                deathClaim!, session.CharacterId);
            if (effectClaim is not null)
            {
                try
                {
                    appliedKill = await commandBus.SendAsync(
                        new ApplyMonsterKillCommand(
                            session.AccountId,
                            session.CharacterId,
                            new KilledMonster(
                                verifiedKill.Code,
                                verifiedKill.Level,
                                verifiedKill.Rank),
                            run.Layout.DungeonId,
                            run.Difficulty,
                            run.Layout.ExperienceWeight,
                            verifiedKill.IsMonster
                                ? HuntEnemyTargetTypes.Monster
                                : HuntEnemyTargetTypes.AiCharacter,
                            run.PartyMemberCount,
                            deathClaim!.ActorState?.PendingLoot,
                            run.PartyKey,
                            run.IsBossRoom && verifiedKill.IsMonster
                                && verifiedKill.Rank == MonsterRank.Boss),
                        cancellationToken).ConfigureAwait(false);
                    if (!runs.CommitParticipantKillEffect(effectClaim))
                    {
                        throw new InvalidOperationException(
                            "The participant kill effect claim was no longer current.");
                    }
                }
                catch
                {
                    runs.ReleaseParticipantKillEffect(effectClaim);
                    var restored = runs.ReleaseActorDeath(deathClaim!);
                    logger.LogWarning(
                        "Monster death {ConnectionId}: durable kill transaction failed; "
                            + "actor={Actor} claim restored={Restored}",
                        context.Connection.ConnectionId,
                        message.ActorSequence,
                        restored);
                    throw;
                }
            }

            award = appliedKill?.Award;
            logger.LogInformation(
                "Kill experience character={CharacterId}: monster={Monster} "
                    + "level={MonsterLevel} rank={Rank} award={Award}",
                session.CharacterId.Value,
                verifiedKill.Code,
                verifiedKill.Level,
                verifiedKill.Rank,
                award);

            foreach (var advance in appliedKill?.QuestAdvances ?? [])
            {
                logger.LogInformation(
                    "Monster death {ConnectionId}: quest={QuestId} trigger={Before}->{After} "
                        + "dungeon={DungeonId} enemy={EnemyCode} type={EnemyType}",
                    context.Connection.ConnectionId,
                    advance.QuestId,
                    advance.Before,
                    advance.After,
                    run.Layout.DungeonId,
                    verifiedKill.Code,
                    verifiedKill.IsMonster
                        ? HuntEnemyTargetTypes.Monster
                        : HuntEnemyTargetTypes.AiCharacter);
            }

            var deathChange = runs.CommitActorDeathChange(deathClaim!)
                ?? throw new InvalidOperationException(
                    "The actor-death claim was no longer current after its durable effects.");
            run = deathChange.Run;
            autoKilled = deathChange.AutoKilledActorSequences;
            if (verifiedKill.IsMonster)
            {
                dropped = [.. deathChange.AddedGroundItems.Select(item => item.ToMonsterDrop())];
                instance = GameDropIdentity.ForKill(run, message.ActorSequence);
            }
        }
        else if (deathClaim is not null)
        {
            var deathChange = runs.CommitActorDeathChange(deathClaim)
                ?? throw new InvalidOperationException(
                    "The actor-death claim was no longer current after validation.");
            run = deathChange.Run;
            autoKilled = deathChange.AutoKilledActorSequences;
        }

        logger.LogInformation(
            "Monster death {ConnectionId} user={WireUserId}: actor={Actor} killedBy={KilledBy}",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.ActorSequence,
            message.WireUserId);

        // Keep the compatibility projection aligned with the award. Without an award, ask the
        // character for its current progress rather than reporting the old projection.
        var currentProgress = award is null
            ? await characters.Get(session.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false)
            : new CharacterProgressionState(award.Level, award.Total);
        if (currentProgress is null)
            return;
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

            // A level decides which quests are offered, so crossing one has to be followed by
            // working that out again -- otherwise a quest whose band the character has just
            // entered stays hidden until they next enter the world.
            if (award.LeveledUp)
            {
                // And it decides how many skill points the character has, so the figure asked for
                // before the award is now a level out of date. Asked again here rather than every
                // time, because a level is the only input to that ledger a kill can move: what has
                // been learned and what bonus is stored are untouched by one. A character that has
                // gone in the meantime keeps the answer already in hand, exactly as the level and
                // the total above keep theirs.
                mirror = await character.RemainingSkillPointsAsync(cancellationToken)
                    .ConfigureAwait(false) ?? mirror;

                result = await offers
                    .RefreshAsync(result, session.AccountId, session.CharacterId, level,
                        cancellationToken).ConfigureAwait(false);
            }

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
            new MonsterDeathNotification(
                message.ActorSequence,
                dropped,
                session.WireUserId,
                instance,
                run is null
                    ? new Dictionary<ushort, ushort>()
                    : DropOwners(dropped)),
        ];
        frames.AddRange(autoKilled.Select(MonsterDeathNotification.Empty));
        AppendQuestGrantFrames(frames, appliedKill);

        if (award?.LeveledUp == true)
        {
            frames.Add(new AvailableQuestListMessage(
                level, result.World.Content.AvailableQuests));
        }

        DungeonClearChange? clear = null;
        if (run is not null
            && await clearEvaluator.EvaluateAsync(
                session.AccountId,
                session.CharacterId,
                run.RunId,
                run.RoomVersion,
                cancellationToken).ConfigureAwait(false) is { } opened)
        {
            clear = opened;
        }

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(frames, []),
            cancellationToken).ConfigureAwait(false);
        if (clear is not null)
        {
            _ = await clearProjection.DeliverAsync(session, clear, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask HandlePartyDeathAsync(
        NetworkDispatchContext context,
        MonsterDeathRequest message,
        SelectedSessionIdentity reporter,
        DungeonRunState initialRun,
        CancellationToken cancellationToken)
    {
        var recipients = new List<PartyDeathRecipient>();
        foreach (var frozen in partyRecipients?.Find(reporter, initialRun.RunId)
                     ?? runDeliveries.Find(initialRun.RunId))
        {
            var result = await currentViews.TryLoadAsync(frozen, cancellationToken).ConfigureAwait(false);
            if (selectedSessions.FindByCharacter(frozen.CharacterId) != frozen
                || result is null)
            {
                continue;
            }

            var mirror = await characters.Get(frozen.CharacterId)
                .RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false);
            recipients.Add(new PartyDeathRecipient(frozen, result, mirror));
        }

        var reporterRecipient = recipients.SingleOrDefault(value => value.Session == reporter);
        if (reporterRecipient is null
            || recipients.Any(recipient => recipient.SkillPoints is null))
        {
            logger.LogWarning(
                "Party monster death {ConnectionId}: every attached participant requires a "
                    + "skill-point mirror; acknowledging actor={Actor} without consuming it",
                context.Connection.ConnectionId,
                message.ActorSequence);
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [MonsterDeathNotification.Empty(message.ActorSequence)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var deathClaim = runs.TryClaimActorDeath(reporter.CharacterId, message.ActorSequence);
        if (deathClaim is null)
        {
            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [MonsterDeathNotification.Empty(message.ActorSequence)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        IdentifiedKill? killed;
        try
        {
            killed = await this.KilledAsync(
                context, deathClaim.Run, message.ActorSequence, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            runs.ReleaseActorDeath(deathClaim);
            throw;
        }

        var results = recipients
            .Select(recipient => new
            {
                recipient.Session.CharacterId,
                Result = runDeliveries.FindKillResult(
                    deathClaim.RunId, deathClaim.Actor, recipient.Session.CharacterId),
            })
            .Where(value => value.Result is not null)
            .ToDictionary(value => value.CharacterId, value => value.Result!);
        if (killed is { } verifiedKill)
        {
            foreach (var recipient in recipients)
            {
                if (selectedSessions.FindByCharacter(recipient.Session.CharacterId)
                        != recipient.Session)
                {
                    continue;
                }

                var effectClaim = runs.TryClaimParticipantKillEffect(
                    deathClaim, recipient.Session.CharacterId);
                if (effectClaim is null)
                    continue;

                try
                {
                    var applied = await commandBus.SendAsync(
                        new ApplyMonsterKillCommand(
                            recipient.Session.AccountId,
                            recipient.Session.CharacterId,
                            new KilledMonster(
                                verifiedKill.Code,
                                verifiedKill.Level,
                                verifiedKill.Rank),
                            deathClaim.Run.Layout.DungeonId,
                            deathClaim.Run.Difficulty,
                            deathClaim.Run.Layout.ExperienceWeight,
                            verifiedKill.IsMonster
                                ? HuntEnemyTargetTypes.Monster
                                : HuntEnemyTargetTypes.AiCharacter,
                            deathClaim.Run.PartyMemberCount,
                            deathClaim.ActorState?.PendingLoot,
                            deathClaim.Run.PartyKey,
                            deathClaim.Run.IsBossRoom && verifiedKill.IsMonster
                                && verifiedKill.Rank == MonsterRank.Boss),
                        cancellationToken).ConfigureAwait(false);
                    if (!runs.CommitParticipantKillEffect(effectClaim))
                    {
                        throw new InvalidOperationException(
                            "The participant kill effect claim was no longer current.");
                    }
                    results[recipient.Session.CharacterId] = applied;
                    runDeliveries.RecordKillResult(
                        deathClaim.RunId,
                        deathClaim.Actor,
                        recipient.Session.CharacterId,
                        applied);
                    this.LogAppliedKill(context, recipient.Session, deathClaim.Run, verifiedKill, applied);
                }
                catch
                {
                    runs.ReleaseParticipantKillEffect(effectClaim);
                    var restored = runs.ReleaseActorDeath(deathClaim);
                    logger.LogWarning(
                        "Party monster death {ConnectionId}: participant={CharacterId} durable "
                            + "effect failed; actor={Actor} claim released={Released}; completed "
                            + "participant effects remain fixed for a selective retry",
                        context.Connection.ConnectionId,
                        recipient.Session.CharacterId.Value,
                        message.ActorSequence,
                        restored);
                    throw;
                }
            }
        }

        var deathChange = runs.CommitActorDeathChange(deathClaim)
            ?? throw new InvalidOperationException(
                "The actor-death claim was no longer current after its durable effects.");
        var run = deathChange.Run;
        var dropped = killed is { IsMonster: true }
            ? deathChange.AddedGroundItems.Select(item => item.ToMonsterDrop()).ToArray()
            : [];
        var instance = killed is { IsMonster: true }
            ? GameDropIdentity.ForKill(run, message.ActorSequence)
            : 0u;

        var sharedDeath = new MonsterDeathNotification(
            message.ActorSequence,
            dropped,
            reporter.WireUserId,
            instance,
            DropOwners(dropped));
        IReadOnlyList<IGameServerMessage> autoDeaths =
            [.. deathChange.AutoKilledActorSequences.Select(MonsterDeathNotification.Empty)];

        var framesByCharacter = new Dictionary<CharacterId, IReadOnlyList<IGameServerMessage>>();
        foreach (var recipient in recipients)
        {
            var frames = await this.PartyFramesAsync(
                recipient,
                results.GetValueOrDefault(recipient.Session.CharacterId),
                sharedDeath,
                autoDeaths,
                cancellationToken).ConfigureAwait(false);
            framesByCharacter[recipient.Session.CharacterId] = frames;
        }

        var requiredFrames = selectedSessions.FindByCharacter(reporter.CharacterId) == reporter
            && framesByCharacter.TryGetValue(reporter.CharacterId, out var reporterFrames)
                ? reporterFrames
                : [];
        var secondary = recipients
            .Where(value => value.Session != reporter)
            .Where(value => selectedSessions.FindByCharacter(value.Session.CharacterId)
                == value.Session)
            .Where(value => framesByCharacter.ContainsKey(value.Session.CharacterId))
            .Select(value => new GameExplicitMessageTarget(
                value.Session.ConnectionId,
                framesByCharacter[value.Session.CharacterId]))
            .ToArray();

        var clear = await clearEvaluator.EvaluateAsync(
            reporter.AccountId,
            reporter.CharacterId,
            run.RunId,
            run.RoomVersion,
            cancellationToken).ConfigureAwait(false);

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(requiredFrames, secondary),
            cancellationToken).ConfigureAwait(false);
        runDeliveries.CompleteKillDelivery(deathClaim.RunId, deathClaim.Actor);
        if (clear is not null)
        {
            _ = await clearProjection.DeliverAsync(reporter, clear, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<IGameServerMessage>> PartyFramesAsync(
        PartyDeathRecipient recipient,
        ApplyMonsterKillResult? applied,
        MonsterDeathNotification sharedDeath,
        IReadOnlyList<IGameServerMessage> clearedFrames,
        CancellationToken cancellationToken)
    {
        var award = applied?.Award;
        var currentProgress = award is null
            ? await characters.Get(recipient.Session.CharacterId)
                .ProgressAsync(cancellationToken).ConfigureAwait(false)
            : new CharacterProgressionState(award.Level, award.Total);
        var frames = new List<IGameServerMessage>();
        if (currentProgress is not null && recipient.SkillPoints is { } originalMirror)
        {
            var level = currentProgress.Level;
            var total = currentProgress.Experience;
            var result = recipient.Runtime;
            var mirror = originalMirror;
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
                if (award.LeveledUp)
                {
                    mirror = await characters.Get(recipient.Session.CharacterId)
                        .RemainingSkillPointsAsync(cancellationToken).ConfigureAwait(false) ?? mirror;
                    result = await offers.RefreshAsync(
                        result,
                        recipient.Session.AccountId,
                        recipient.Session.CharacterId,
                        level,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            frames.Add(new ExperienceNotification(
                level,
                diagnostics.LabelProbe ? total + ExperienceDiagnostics.ProbeTotal : total,
                mirror,
                AccountHonorLevel.Initial,
                ExperienceBonusLines.Of(award),
                diagnostics.LabelProbe));
            frames.Add(sharedDeath);
            if (award?.LeveledUp == true)
            {
                frames.Add(new AvailableQuestListMessage(
                    level, result.World.Content.AvailableQuests));
            }
        }
        else
        {
            frames.Add(sharedDeath);
        }
        AppendQuestGrantFrames(frames, applied);
        frames.AddRange(clearedFrames);
        return frames;
    }

    private static void AppendQuestGrantFrames(
        List<IGameServerMessage> frames,
        ApplyMonsterKillResult? applied)
    {
        if (applied?.QuestItems is { } items)
            frames.Add(GameInventoryDelivery.Notification(items));
        if (applied?.AccountMaterials is { Count: > 0 } accountMaterials)
        {
            frames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [.. accountMaterials.Select(material => new InventoryRuntimeValueState(
                    material.VirtualSlotIndex,
                    material.ItemId,
                    material.FinalCount))],
                DateTimeOffset.UtcNow));
        }
        var quests = applied?.QuestProgress?.Quests;
        if (quests is not null)
        {
            frames.Add(new AcceptedQuestProgressMessage(
                [.. quests.OrderBy(quest => quest.Slot)
                    .Select(quest => new AcceptedQuestProgressState(
                        checked((ushort)quest.QuestId),
                        unchecked((uint)quest.TriggerValue)))]));
        }
    }

    private void LogAppliedKill(
        NetworkDispatchContext context,
        SelectedSessionIdentity session,
        DungeonRunState run,
        IdentifiedKill killed,
        ApplyMonsterKillResult applied)
    {
        logger.LogInformation(
            "Party kill experience character={CharacterId}: monster={Monster} "
                + "level={MonsterLevel} rank={Rank} award={Award} party={PartyMemberCount}",
            session.CharacterId.Value,
            killed.Code,
            killed.Level,
            killed.Rank,
            applied.Award,
            run.PartyMemberCount);
        foreach (var advance in applied.QuestAdvances)
        {
            logger.LogInformation(
                "Party monster death {ConnectionId}: character={CharacterId} quest={QuestId} "
                    + "trigger={Before}->{After} dungeon={DungeonId} enemy={EnemyCode} "
                    + "type={EnemyType}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                advance.QuestId,
                advance.Before,
                advance.After,
                run.Layout.DungeonId,
                killed.Code,
                killed.IsMonster
                    ? HuntEnemyTargetTypes.Monster
                    : HuntEnemyTargetTypes.AiCharacter);
        }
    }

    private static IReadOnlyDictionary<ushort, ushort> DropOwners(
        IReadOnlyList<MonsterDrop> drops)
    {
        if (drops.All(drop => drop.Rolled.OwnerCharacterId is null))
            return new Dictionary<ushort, ushort>();

        return drops
            .Where(drop => drop.Rolled.OwnerCharacterId is { })
            .ToDictionary(
                drop => drop.Number,
                drop => SelectedSessionIdentity.ToWireUserId(
                    drop.Rolled.OwnerCharacterId!.Value));
    }

    private sealed record PartyDeathRecipient(
        SelectedSessionIdentity Session,
        SelectCharacterResult Runtime,
        CharacterSkillProtocolMirrorState? SkillPoints);

    /// <summary>
    /// What died, or nothing when the room cannot name it.
    /// </summary>
    /// <remarks>
    /// The monster is looked up in the room the client is standing in, by the running number that
    /// room's actors were given. A sequence the room does not account for yields nothing: it means
    /// the client and this server disagree about what is in the room, and paying an invented
    /// monster would turn that disagreement into experience and items.
    /// <para>
    /// Answered once and used twice, because experience and drops are decided from the same three
    /// facts -- what it was, what level, what rank -- and looking it up separately for each would
    /// let the two disagree.
    /// </para>
    /// </remarks>
    private async ValueTask<IdentifiedKill?> KilledAsync(
        NetworkDispatchContext context,
        DungeonRunState run,
        ushort actorSequence,
        CancellationToken cancellationToken)
    {
        var lookup = await kills.IdentifyAsync(run, actorSequence, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Kill is not { } killed)
        {
            logger.LogWarning(
                "Monster death {ConnectionId}: actor {Actor} is not one of the {Count} this room "
                    + "declares, so nothing was awarded and nothing dropped",
                context.Connection.ConnectionId, actorSequence, lookup.DeclaredCount);
            return null;
        }

        if (!killed.IsMonster)
        {
            logger.LogInformation(
                "Monster death {ConnectionId}: actor {Actor} is an AI character, not a monster; "
                    + "only server-verified quest progress may be recorded",
                context.Connection.ConnectionId, actorSequence);
        }

        return killed;
    }

}
