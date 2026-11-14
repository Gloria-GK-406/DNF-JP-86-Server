using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Networking.Tests.Characterization;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// A duel lobby client's view of the rooms on its listener, built only from the frames it read: <c>0x0029</c> rooms
/// whole, <c>0x002B</c> seats (and the mode), <c>0x002A</c> owner, map, mode and state, state 0 a room that is gone.
/// </summary>
/// <remarks>
/// Spec RQ-003: at any moment this view, accumulated from increments, must equal the snapshot a session entering the
/// lobby now is given. Layouts are the slice codecs' (protocol pages NOTI 0x0029/0x002A/0x002B).
/// </remarks>
internal sealed class PvpLobbyWireView
{
    private readonly SortedDictionary<ushort, Room> rooms = [];

    public static PvpLobbyWireView Of(IEnumerable<WireFrame> frames)
    {
        var view = new PvpLobbyWireView();
        foreach (var frame in frames.Where(frame => frame.IsNoti))
        {
            switch (frame.Type)
            {
                case 0x0029: view.ApplyRooms(frame.Body); break;
                case 0x002A: view.ApplyRoomState(frame.Body); break;
                case 0x002B: view.ApplySeats(frame.Body); break;
            }
        }
        return view;
    }

    public IReadOnlyCollection<ushort> Numbers => rooms.Keys;

    /// <summary>The seat holding <paramref name="user"/> in room <paramref name="number"/>.</summary>
    public int SeatOf(ushort number, ushort user) =>
        Array.FindIndex(rooms[number].Seats, seat => seat.User == user && seat.State is <= 2);

    public byte StateOf(ushort number) => rooms[number].State;

    public string Render() =>
        rooms.Count == 0 ? "(no rooms)" : string.Join(Environment.NewLine, rooms.Select(entry => $"{entry.Key}: {entry.Value}"));

    private void ApplyRooms(ReadOnlySpan<byte> body)
    {
        var offset = 0;
        var count = U16(body, ref offset);
        for (var index = 0; index < count; index++)
        {
            var number = U16(body, ref offset);
            var room = new Room { NameType = body[offset++] };
            if (room.NameType == 0)
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
                offset += 4;
                room.Name = Encoding.ASCII.GetString(body.Slice(offset, length));
                offset += length;
            }
            room.State = body[offset++];
            room.Owner = body[offset++];
            room.Map = BinaryPrimitives.ReadInt16LittleEndian(body[offset..]);
            offset += 2;
            room.Mode = body[offset++];
            for (var seat = 0; seat < 8; seat++)
            {
                var state = body[offset++];
                room.Seats[seat] = (state, U16(body, ref offset));
            }
            room.Password = body[offset++];
            room.Matching = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
            offset += 4;
            rooms[number] = room;
        }
        Assert.Equal(body.Length, offset);
    }

    private void ApplyRoomState(ReadOnlySpan<byte> body)
    {
        Assert.Equal(11, body.Length);
        var number = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (body[2] == 0)
        {
            rooms.Remove(number);
            return;
        }
        var room = rooms[number];
        room.State = body[2];
        room.Owner = body[3];
        room.Map = BinaryPrimitives.ReadInt16LittleEndian(body[4..]);
        room.Mode = body[6];
        room.Matching = BinaryPrimitives.ReadInt32LittleEndian(body[7..]);
    }

    private void ApplySeats(ReadOnlySpan<byte> body)
    {
        var number = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var room = rooms[number];
        room.Mode = body[2];
        var count = body[3];
        Assert.Equal(4 + count * 4, body.Length);
        for (var index = 0; index < count; index++)
        {
            var offset = 4 + index * 4;
            room.Seats[body[offset]] = (body[offset + 1], BinaryPrimitives.ReadUInt16LittleEndian(body[(offset + 2)..]));
        }
    }

    private static ushort U16(ReadOnlySpan<byte> body, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
        offset += 2;
        return value;
    }

    private sealed class Room
    {
        public byte NameType { get; init; }
        public string Name { get; set; } = "";
        public byte State { get; set; }
        public byte Owner { get; set; }
        public short Map { get; set; }
        public byte Mode { get; set; }
        public (byte State, ushort User)[] Seats { get; } = new (byte, ushort)[8];
        public byte Password { get; set; }
        public int Matching { get; set; }

        public override string ToString() =>
            $"name={NameType}/'{Name}' state={State} owner={Owner} map={Map} mode={Mode} password={Password} matching={Matching} seats=["
            + string.Join(" ", Seats.Select(seat => $"{seat.State:X2}:{seat.User}")) + "]";
    }
}
