using System.Buffers.Binary;

namespace GameplayLoad;

/// <summary>
/// Request bodies and first-reply signatures for the requests the load sends.
/// </summary>
/// <remarks>
/// Bodies are the ones TASK-01's characterization drives on the wire
/// (Server/tests/Dfo.Server.Networking.Tests/Characterization/CharacterizationSteps.cs and WireClient.cs).
/// A signature names the requester's own first reply as the golden transcripts in
/// Characterization/Golden/*.hex show it. Town NOTIs that other players' movement also produces
/// (0x0016, 0x0017) are matched on the leading wire user id, so another player's projection cannot
/// stop this request's clock. Requests with no own reply are measured with a step fence and counted
/// as "no reply" when the signature has not matched before the fence answer.
/// </remarks>
internal static class Protocol
{
    public const ushort Login = 0x0001;
    public const ushort SelectCharacter = 0x0004;
    public const ushort CreateCharacter = 0x0005;
    public const ushort ReturnToSelection = 0x0007;
    public const ushort RequestPeer = 0x000A;
    public const ushort ResponsePeer = 0x000B;
    public const ushort LeaveParty = 0x000D;
    public const ushort EnterSelectDungeon = 0x000F;
    public const ushort SelectDungeon = 0x0010;
    public const ushort SetUserPosition = 0x0023;
    public const ushort SetUserArea = 0x0024;
    public const ushort FinishLoading = 0x0025;
    public const ushort Eplp = 0x0048;
    public const ushort Fence = WireConnection.FenceType;

    /// <summary>Story-mode dungeon open from level 1 (TASK-01 scenario (c)).</summary>
    public const int PartyDungeonId = 144;

    public static string Name(ushort type) => type switch
    {
        Login => "LOGIN",
        SelectCharacter => "SELECT_CHARACTER",
        CreateCharacter => "CREATE_CHARACTER",
        ReturnToSelection => "RETURN_SELECT_CHARACTER",
        RequestPeer => "REQUEST_PEER",
        ResponsePeer => "RESPONSE_PEER",
        LeaveParty => "LEAVE_PARTY",
        EnterSelectDungeon => "ENTER_SELECT_DUNGEON",
        SelectDungeon => "SELECT_DUNGEON",
        SetUserPosition => "SET_USER_POSITION",
        SetUserArea => "SET_USER_AREA",
        FinishLoading => "FINISH_LOADING",
        Eplp => "EPLP",
        Fence => "CHECK_USER_CONNECTION",
        _ => $"0x{type:X4}",
    };

    public static byte[] LoginBody(string mId, string ticket) => WireConnection.DStrings(mId, ticket);

    /// <summary>CREATE_CHARACTER with job 0, WireSmoke's body.</summary>
    public static byte[] CreateBody(string name) => [0x00, .. WireConnection.DStrings(name), 0, 0, 0, 0];

    public static byte[] SelectBody(int rosterIndex) => U16((ushort)rosterIndex);

    /// <summary>SET_USER_AREA in town 1. Offset 9 is the area being left (protocol/game/client-to-server/0x0024.md).</summary>
    public static byte[] AreaBody(byte area, byte leaving)
    {
        var body = Convert.FromHexString("01013E07FF00050100000000000000");
        body[1] = area;
        body[9] = leaving;
        return body;
    }

    /// <summary>SET_USER_POSITION: x, y, direction, trailer (TownProjectionSlice.SetUserPositionDecoder).</summary>
    public static byte[] PositionBody(short x, short y, byte direction)
    {
        var body = new byte[7];
        BinaryPrimitives.WriteInt16LittleEndian(body, x);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2), y);
        body[4] = direction;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(5), 100);
        return body;
    }

    public static byte[] RequestPeerBody(ushort invitee) => [.. U16(invitee), 0x00, 0x40, 0, 0, 0, 0, 0];

    public static byte[] ResponsePeerBody(ushort inviter) => [.. U16(inviter), 0x00, 0, 0, 0, 0];

    public static byte[] EnterSelectDungeonBody() => [0, 0, 0, 0];

    public static byte[] SelectDungeonBody(int dungeonId) => [.. U32((uint)dungeonId), 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0];

    public static byte[] FinishLoadingBody() => new byte[8];

    /// <summary>EPLP body 0101: leave the run and go back to town (TASK-01 scenario (d)).</summary>
    public static byte[] LeaveDungeonBody() => [0x01, 0x01];

    // ---- first-reply signatures -------------------------------------------------------------

    public static Func<Frame, bool> Cmd(ushort type) => frame => frame.Is(0x01, type);

    public static Func<Frame, bool> Noti(ushort type) => frame => frame.Is(0x00, type);

    /// <summary>NOTI 0x0016 carrying this player's own wire id: the answer to its own position report.</summary>
    public static Func<Frame, bool> OwnPosition(ushort wireId) => frame => frame.Is(0x00, 0x0016) && frame.LeadingU16 == wireId;

    /// <summary>NOTI 0x0017 carrying this player's own wire id: the answer to its own area crossing.</summary>
    public static Func<Frame, bool> OwnArea(ushort wireId) => frame => frame.Is(0x00, 0x0017) && frame.LeadingU16 == wireId;

    /// <summary>
    /// ENTER_SELECT_DUNGEON and SELECT_DUNGEON open with the requester's full USERINFO (519-byte body);
    /// town arrivals of other players carry the 190-byte form. A CMD of the request's own type is a
    /// refusal or the acknowledgement, whichever comes first.
    /// </summary>
    public static Func<Frame, bool> DungeonFirst(ushort type) =>
        frame => (frame.Is(0x00, 0x0002) && frame.Body.Length >= 500) || frame.Is(0x01, type);

    public static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
