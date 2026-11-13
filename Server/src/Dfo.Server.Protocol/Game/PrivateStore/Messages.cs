using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PrivateStore;

// Requests. Layouts: CMD 0x55 and 0x57 captured 2026-11-14 from the A21 client (WireCaptured, see
// protocol/game/client-to-server/0x0055.md, 0x0057.md); the rest are the 70 dispatcher reads
// (70Reference, Document 拍卖行调研-20261113/个人商店调研.md §2) with the reference server's A21 widths.

public interface IPrivateStoreRequest : IClientNetworkMessage
{
    GameMessageMetadata Metadata { get; }

    bool Malformed { get; }
}

/// <summary>CMD 0x55: <c>u8 town, u8 area, i16 x, i16 y, u16 doll slot (0xFFFF none)</c>.</summary>
public sealed record PrivateStoreCreateRequest(GameMessageMetadata Metadata, bool Malformed, byte TownId, byte AreaId, short X, short Y,
    BusinessSlot? Doll) : IPrivateStoreRequest
{
    public ClientMessageType MessageType => ClientMessageType.GamePrivateStoreCreate;
}

/// <summary>CMD 0x57: <c>i32 title length, title, u8 n, n × (u16 display, i32 price, u8 space, i16 slot, i32 count), u16 doll slot</c>.</summary>
/// <remarks>The client leaves the count of a single item unset (seen: 0 and garbage for two pieces of equipment).</remarks>
public sealed record PrivateStoreDisplayRequest(GameMessageMetadata Metadata, bool Malformed, byte[] Title,
    IReadOnlyList<PrivateStoreDisplayLine> Lines) : IPrivateStoreRequest
{
    public ClientMessageType MessageType => ClientMessageType.GamePrivateStoreCompleteDisplay;
}

/// <summary>CMD 0x53 (enter, by the owner's wire user id), 0x54 (exit), 0x56 (close), 0x58 (back to the gate).</summary>
public sealed record PrivateStoreSimpleRequest(GameMessageMetadata Metadata, bool Malformed, ClientMessageType Type, ushort Owner = 0)
    : IPrivateStoreRequest
{
    public ClientMessageType MessageType => Type;
}

/// <summary>CMD 0x52 (buy) and 0x195 (check before buying): <c>u16 display slot, i32 count</c>.</summary>
public sealed record PrivateStoreLineRequest(GameMessageMetadata Metadata, bool Malformed, bool Buy, byte DisplaySlot, int Count)
    : IPrivateStoreRequest
{
    public ClientMessageType MessageType => Buy ? ClientMessageType.GamePrivateStoreBuy : ClientMessageType.GamePrivateStoreVerify;
}

// Replies: success starts with 1, a refusal is [0][code] (70 MakeErrorReturnPacket).

/// <summary>A reply to any stall request.</summary>
/// <param name="Body">The body after the envelope byte, or null for a refusal.</param>
public sealed record PrivateStoreReply(ServerMessageType MessageType, byte[]? Body, byte Code = 0) : IGameCmdMessage;

/// <summary>A stall notice to the area or the stall's visitors.</summary>
public sealed record PrivateStoreNotice(ServerMessageType MessageType, byte[] Body) : IGameNotiMessage;
