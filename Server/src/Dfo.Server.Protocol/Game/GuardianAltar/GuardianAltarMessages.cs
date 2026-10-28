using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

public enum GuardianAltarOperation : ushort { Open = 0x0392, Buy, SetSlot, UpgradeEnergy, Summon, Exchange, Pause, Achievement, Reset }
public sealed record GuardianAltarRequest(GameMessageMetadata Metadata, GuardianAltarOperation Operation,
    int RidableId = 1, int Kind = 0, int ItemId = 0, byte Slot = 0, bool Attach = true,
    int FirstLevel = 0, int LastLevel = 0, int SecondKind = 0, byte SecondSlot = 0, int SecondItemId = 0,
    int EnergyLevel = 0) : IClientNetworkMessage
{ public ClientMessageType MessageType => (ClientMessageType)(0x0001_0000 | (int)Operation); }

internal static class GuardianAltarRequestCodec
{
    public static GuardianAltarRequest? Decode(GameMessageMetadata metadata, GuardianAltarOperation operation, ReadOnlySpan<byte> body)
    {
        var length = operation switch { GuardianAltarOperation.Open => 0, GuardianAltarOperation.Buy => 30,
            GuardianAltarOperation.SetSlot => 28, GuardianAltarOperation.UpgradeEnergy => 22,
            GuardianAltarOperation.Summon => 23, GuardianAltarOperation.Exchange => 37,
            GuardianAltarOperation.Pause => 15, GuardianAltarOperation.Achievement or GuardianAltarOperation.Reset => 18, _ => -1 };
        if (body.Length != length) return null;
        var request = new GuardianAltarRequest(metadata, operation);
        return operation switch
        {
            GuardianAltarOperation.Open => request,
            GuardianAltarOperation.Buy => request with { RidableId = I(body, 14), Kind = I(body, 18), ItemId = I(body, 22),
                FirstLevel = BinaryPrimitives.ReadInt16LittleEndian(body[26..]), LastLevel = BinaryPrimitives.ReadInt16LittleEndian(body[28..]) },
            GuardianAltarOperation.SetSlot when body[18] <= 1 => request with { RidableId = I(body, 14), Attach = body[18] != 0,
                Kind = I(body, 19), Slot = body[23], ItemId = I(body, 24) },
            GuardianAltarOperation.Summon => request with { Slot = body[14], ItemId = I(body, 15), EnergyLevel = I(body, 19) },
            GuardianAltarOperation.Exchange when body[18] <= 1 => request with { RidableId = I(body, 14), Attach = body[18] != 0,
                Kind = I(body, 19), Slot = body[23], ItemId = I(body, 24), SecondKind = I(body, 28),
                SecondSlot = body[32], SecondItemId = I(body, 33) },
            GuardianAltarOperation.Pause when body[14] <= 1 => request with { Attach = body[14] != 0 },
            GuardianAltarOperation.Achievement or GuardianAltarOperation.Reset => request with { ItemId = I(body, 14) },
            GuardianAltarOperation.UpgradeEnergy => request with { EnergyLevel = I(body, 14) },
            _ => null,
        };
    }
    private static int I(ReadOnlySpan<byte> body, int offset) => BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
}
internal sealed class GuardianAltarOpenDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Open, b); }
internal sealed class GuardianAltarBuyDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Buy, b); }
internal sealed class GuardianAltarSetSlotDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.SetSlot, b); }
internal sealed class GuardianAltarUpgradeDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.UpgradeEnergy, b); }
internal sealed class GuardianAltarSummonDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Summon, b); }
internal sealed class GuardianAltarExchangeDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Exchange, b); }
internal sealed class GuardianAltarPauseDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Pause, b); }
internal sealed class GuardianAltarAchievementDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Achievement, b); }
internal sealed class GuardianAltarResetDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata m, ReadOnlySpan<byte> b) => GuardianAltarRequestCodec.Decode(m, GuardianAltarOperation.Reset, b); }

internal sealed record GuardianAltarOpenResponse(GuardianAltarProgressView? View)
    : GameStateSynchronizationMessage, IGameCmdMessage
{ internal override ushort Type => 0x0392; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Open(View); }
internal sealed record GuardianAltarActionResponse(GuardianAltarRequest Request, int Result, GuardianAltarProgress? Progress,
    GuardianAltarRunState? Run) : GameStateSynchronizationMessage, IGameCmdMessage
{ internal override ushort Type => (ushort)Request.Operation; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Response(Request, Result, Progress, Run); }
internal sealed record GuardianAltarProfileMessage(GuardianAltarProgressView View)
    : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x0362; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.ProfileUpdate(View); }
internal sealed record GuardianAltarStageInfoMessage(GuardianAltarAdmission Admission) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x035B; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.StageInfo(Admission); }
internal sealed record GuardianAltarMapMessage(DungeonRunState Run, GuardianAltarAdmission Admission) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x035C; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Map(Run, Admission); }
internal sealed record GuardianAltarSpawnMessage(ushort Sequence, GuardianAltarSpawnView View)
    : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x035D; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Spawn(Sequence, View); }
internal sealed record GuardianAltarResultMessage(GuardianAltarActionResult Action, GuardianAltarProgressView View)
    : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x0361; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Result(Action, View); }
internal sealed record GuardianAltarEplpMessage(GuardianAltarRunState Run, bool HasTickets) : GameStateSynchronizationMessage, IGameNotiMessage
{ internal override ushort Type => 0x0360; internal override byte[] EncodeBody() => GuardianAltarPayloadCodec.Eplp(Run, HasTickets); }
