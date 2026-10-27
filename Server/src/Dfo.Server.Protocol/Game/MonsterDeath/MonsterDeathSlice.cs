using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game;
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
/// A21 starts with a u32 actor number or passive-object template code and a u16 killer.
/// The flag after the variable attack list distinguishes object destruction from monster death.
/// Object reports carry no proven instance identifier and claim a pre-generated Cell queue entry.
/// </remarks>
public sealed record MonsterDeathRequest(
    GameMessageMetadata Metadata,
    ushort ActorSequence,
    ushort WireUserId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMonsterDeath;
    public int? ObjectCode { get; init; }
    // Server startup measurement input, never decoded from client bytes.
    internal bool LabelProbe { get; init; }

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
        if (award is null) return None;
        var b = award.Breakdown;
        var amounts = new uint[KindCount];
        amounts[(int)ExperienceBonusKind.PartyReward] = b.Party;
        amounts[(int)ExperienceBonusKind.FatigueBuff] = b.FatigueBuff;
        amounts[(int)ExperienceBonusKind.SeriaBlessing] = b.Channel;
        amounts[(int)ExperienceBonusKind.GrowthContract] = b.GrowthContract;
        amounts[(int)ExperienceBonusKind.FatigueBurn] = b.FatigueBurning;
        amounts[(int)ExperienceBonusKind.InternetCafe] = b.InternetCafe;
        amounts[(int)ExperienceBonusKind.RapidGrowthBuff] = b.RapidGrowthBuff;
        amounts[(int)ExperienceBonusKind.SeriaWelcome] = b.SeriaWelcome;
        amounts[(int)ExperienceBonusKind.GuildReward] = b.Guild;
        // Potion and clear-rank amounts stay in the base display until their wire slots are proven.
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
    /// <summary>A21 fixed business body before variable attack entries and the trailing checksum.</summary>
    public const int MinimumBodyLength = 62;

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

internal sealed class MonsterDeathDecoder(
    ILogger<MonsterDeathDecoder>? logger = null, ExperienceDiagnostics? diagnostics = null) : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (logger?.IsEnabled(LogLevel.Trace) == true)
            logger.LogTrace(
                "DIE_MONSTER capture metadata={Metadata} body({BodyLength}B)={Body}",
                metadata, body.Length, Convert.ToHexString(body));

        // A21: u32 actor/object code, u16 killer, four u32 values, then count x 10.
        if (body.Length < MonsterDeathPayloadCodec.MinimumBodyLength)
            throw new InvalidDataException("DIE_MONSTER has a truncated A21 prefix.");
        var tail = 23 + body[22] * 10;
        if (body.Length < tail + 39)
            throw new InvalidDataException("DIE_MONSTER has truncated attack records or suffix.");
        var code = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body);
        var killer = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
        var passive = body[tail + 6];
        if (passive > 1 || code > int.MaxValue || (passive == 0 && code > ushort.MaxValue))
            throw new InvalidDataException("DIE_MONSTER contains an invalid actor/object identity.");
        return new MonsterDeathRequest(metadata, passive == 0 ? (ushort)code : (ushort)0, killer)
        {
            ObjectCode = passive == 1 ? (int)code : null,
            LabelProbe = diagnostics?.LabelProbe ?? false,
        };
    }
}

public sealed class MonsterDeathHandler(ICommandBus commands)
    : ClientMessageHandler<MonsterDeathRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameMonsterDeathCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MonsterDeathRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameMonsterDeathCommand(context, message), ct).ConfigureAwait(false);
}
