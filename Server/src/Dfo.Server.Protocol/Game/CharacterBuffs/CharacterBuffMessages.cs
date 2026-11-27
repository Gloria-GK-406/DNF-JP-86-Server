using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CharacterBuffs;

internal sealed record CharacterBuffAdded(int BuffId, int State1) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterBuffAdded;
}

internal sealed record CharacterBuffRemoved(int BuffId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterBuffRemoved;
}

internal sealed record CharacterDungeonBuffs(IReadOnlyList<int> BuffIds) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterDungeonBuffs;
}

internal sealed class CharacterBuffAddedEncoder : GameNotiMessageEncoder<CharacterBuffAdded>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterBuffAdded message)
    {
        var body = new byte[17];
        body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.BuffId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), message.State1);
        return OnePayload(message, body);
    }
}

internal sealed class CharacterBuffRemovedEncoder : GameNotiMessageEncoder<CharacterBuffRemoved>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterBuffRemoved message)
    {
        var body = new byte[5];
        body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), message.BuffId);
        return OnePayload(message, body);
    }
}

internal sealed class CharacterDungeonBuffsEncoder : GameNotiMessageEncoder<CharacterDungeonBuffs>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterDungeonBuffs message)
    {
        var body = new byte[sizeof(ushort) + sizeof(int) * message.BuffIds.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(body, checked((ushort)message.BuffIds.Count));
        for (var index = 0; index < message.BuffIds.Count; index++)
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(sizeof(ushort) + sizeof(int) * index), message.BuffIds[index]);
        return OnePayload(message, body);
    }
}

internal static class CharacterBuffRegistration
{
    internal static void AddCharacterBuffSlice(IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<CharacterBuffAdded, CharacterBuffAddedEncoder>();
        services.AddGameNotiMessageEncoder<CharacterBuffRemoved, CharacterBuffRemovedEncoder>();
        services.AddGameNotiMessageEncoder<CharacterDungeonBuffs, CharacterDungeonBuffsEncoder>();
    }
}
