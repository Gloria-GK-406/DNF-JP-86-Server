using System.Buffers.Binary;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// One entry of one card: an identifier and an amount.
/// </summary>
/// <remarks>
/// What the pair means is decided by where it sits, not by what is in it. The first entry of a
/// card is that card's gold and its <see cref="ItemId"/> is never read; the second is the piece
/// behind the card; anything after that is consumed and never drawn. See
/// <c>protocol/game/server-to-client/NOTI-0x0023.md</c>.
/// </remarks>
public sealed record CardEntry(int ItemId, int Amount);

/// <summary>One column of one row of the card panel, and everything declared on it.</summary>
public sealed record CardColumn(IReadOnlyList<CardEntry> Entries)
{
    /// <summary>What this card pays in gold, or zero when it declares nothing at all.</summary>
    public int Gold => this.Entries.Count > 0 ? this.Entries[0].Amount : 0;

    /// <summary>The piece behind this card, or <c>null</c> when there is none.</summary>
    public CardEntry? Piece => this.Entries.Count > 1 ? this.Entries[1] : null;
}

/// <summary>One row of the card panel: the free row, the gold row, or the black-diamond row.</summary>
public sealed record CardRow(IReadOnlyList<CardColumn> Columns);

/// <summary>The card tail of a clear's reward notification, read the way the client reads it.</summary>
public sealed record ClearRewardTail(
    int AcquisitionRecordCount,
    CardRow FreeRow,
    int PaidFlipCost,
    CardRow GoldRow,
    CardRow BlackDiamondRow,
    IReadOnlyList<byte> Trailer);

/// <summary>One record of a flip answer: who turned each of that column's two cards over.</summary>
/// <param name="FreeSeat">The seat that turned this column's free card, or <c>0xFF</c>.</param>
/// <param name="GoldSeat">The seat that turned this column's gold card, or <c>0xFF</c>.</param>
public sealed record FlipRecord(
    byte FreeSeat, byte GoldSeat, IReadOnlyList<CardEntry> Entries, byte Trailer);

/// <summary>
/// Reads the two card-panel frames the way the client reads them, and refuses anything it cannot
/// walk to the end.
/// </summary>
/// <remarks>
/// <b>This type exists because asserting on byte offsets is what let the last attempt through.</b>
/// A judge that checks "the encoder wrote 31001 at offset 161" agrees with the encoder by
/// construction: both sides carry the same picture of the frame, so a wrong picture passes. This
/// reader carries the client's picture instead -- it consumes counts and lets them decide where
/// everything after them begins -- so a count byte written wrong desynchronises it and every
/// assertion downstream fails.
/// <para>
/// It also refuses to stop early or run past the end. A frame that declares more than it carries,
/// or carries more than it declares, is a frame the client could not walk either, and that is a
/// defect no assertion about a particular field would notice.
/// </para>
/// </remarks>
public static class CardPanelFrameReader
{
    /// <summary>How many columns each row of the tail declares.</summary>
    public const int ColumnsPerRow = 8;

    /// <summary>How many records a flip answer declares.</summary>
    public const int AnswerRecordCount = 8;

    /// <summary>The width of the trailer behind the last row.</summary>
    public const int TrailerLength = 22;

    /// <summary>The width of one item-acquisition record.</summary>
    private const int AcquisitionRecordLength = 15;

    /// <summary>
    /// Walks a whole <c>CLEAR_DUNGEON_REWARD</c> body and returns its card tail.
    /// </summary>
    /// <remarks>
    /// Everything before the tail is walked rather than skipped, because three of the fields in it
    /// are counts: a value written into any of them moves the tail, and a reader that jumped to a
    /// fixed offset would keep passing while the client read the frame apart.
    /// </remarks>
    public static ClearRewardTail ReadClearDungeonReward(ReadOnlySpan<byte> body)
    {
        var at = 0;

        Skip(body, ref at, 4 * 4); // the four headline figures
        Skip(body, ref at, 1);
        Skip(body, ref at, 23 * 4); // the fixed reward slots

        SkipCountedPairs(body, ref at); // first counted list
        SkipCountedPairs(body, ref at); // second counted list
        Skip(body, ref at, 2 * 4);
        Skip(body, ref at, 5 * 4);
        Skip(body, ref at, 4 * 4); // the score quartet

        var objectExperience = ReadInt32(body, ref at);
        Require(objectExperience >= 0, "per-object experience count is negative");
        Skip(body, ref at, objectExperience * 8);

        int acquisitions = ReadByte(body, ref at);
        Skip(body, ref at, acquisitions * AcquisitionRecordLength);

        var freeRow = ReadRow(body, ref at);
        var paidFlipCost = ReadInt32(body, ref at);
        var goldRow = ReadRow(body, ref at);
        var blackDiamondRow = ReadRow(body, ref at);

        var trailer = new byte[TrailerLength];
        for (var index = 0; index < TrailerLength; index++)
            trailer[index] = ReadByte(body, ref at);

        Require(
            at == body.Length,
            $"the frame declares {at} bytes but carries {body.Length}");

        return new ClearRewardTail(
            acquisitions, freeRow, paidFlipCost, goldRow, blackDiamondRow, trailer);
    }

    /// <summary>
    /// Walks a whole <c>SELECT_CARD</c> answer, envelope included, and returns its eight records.
    /// </summary>
    public static IReadOnlyList<FlipRecord> ReadSelectCardAnswer(ReadOnlySpan<byte> body)
    {
        var at = 0;
        var envelope = ReadByte(body, ref at);
        Require(envelope == 0x01, $"the answer's envelope byte is 0x{envelope:X2}, not 0x01");

        var records = new List<FlipRecord>(AnswerRecordCount);
        for (var record = 0; record < AnswerRecordCount; record++)
        {
            var freeSeat = ReadByte(body, ref at);
            var goldSeat = ReadByte(body, ref at);
            var entries = ReadEntries(body, ref at);
            var trailer = ReadByte(body, ref at);
            records.Add(new FlipRecord(freeSeat, goldSeat, entries, trailer));
        }

        Require(
            at == body.Length,
            $"the answer declares {at} bytes but carries {body.Length}");

        return records;
    }

    private static CardRow ReadRow(ReadOnlySpan<byte> body, ref int at)
    {
        var columns = new List<CardColumn>(ColumnsPerRow);
        for (var column = 0; column < ColumnsPerRow; column++)
            columns.Add(new CardColumn(ReadEntries(body, ref at)));

        return new CardRow(columns);
    }

    private static IReadOnlyList<CardEntry> ReadEntries(ReadOnlySpan<byte> body, ref int at)
    {
        int declared = ReadByte(body, ref at);
        var entries = new List<CardEntry>(declared);
        for (var entry = 0; entry < declared; entry++)
        {
            var itemId = ReadInt32(body, ref at);
            var amount = ReadInt32(body, ref at);
            entries.Add(new CardEntry(itemId, amount));
        }

        return entries;
    }

    private static void SkipCountedPairs(ReadOnlySpan<byte> body, ref int at)
    {
        int declared = ReadByte(body, ref at);
        Skip(body, ref at, declared * 8);
    }

    private static byte ReadByte(ReadOnlySpan<byte> body, ref int at)
    {
        Require(at + 1 <= body.Length, $"a byte was read at {at} of a {body.Length} byte frame");
        return body[at++];
    }

    private static int ReadInt32(ReadOnlySpan<byte> body, ref int at)
    {
        Require(
            at + 4 <= body.Length,
            $"four bytes were read at {at} of a {body.Length} byte frame");
        var value = BinaryPrimitives.ReadInt32LittleEndian(body[at..]);
        at += 4;
        return value;
    }

    private static void Skip(ReadOnlySpan<byte> body, ref int at, int count)
    {
        Require(
            at + count <= body.Length,
            $"{count} bytes were skipped at {at} of a {body.Length} byte frame");
        at += count;
    }

    private static void Require(bool condition, string because)
    {
        if (!condition)
            throw new InvalidDataException($"The frame cannot be walked: {because}.");
    }
}
