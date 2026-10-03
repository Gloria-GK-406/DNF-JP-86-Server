using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.IllusionTowerReward;

internal sealed record IllusionTowerRewardNotification(
    uint Experience, IReadOnlyDictionary<byte, ImmutableArray<TowerRewardCandidate>> Slots) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameIllusionTowerReward;
}

internal static class IllusionTowerRewardPayloadCodec
{
    public static byte[] Encode(uint experience,
        IReadOnlyDictionary<byte, ImmutableArray<TowerRewardCandidate>> slots)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(experience);
        for (byte slot = 0; slot < 4; slot++)
        {
            var candidates = slots.GetValueOrDefault(slot, []);
            if (candidates.Length > byte.MaxValue)
                throw new InvalidDataException("Tower reward candidate count cannot be represented as u8.");
            writer.Write(checked((byte)candidates.Length));
            foreach (var candidate in candidates)
            {
                writer.Write(candidate.ItemId);
                writer.Write(candidate.DisplayValue);
            }
        }
        return stream.ToArray();
    }
}

internal sealed class IllusionTowerRewardEncoder : GameNotiMessageEncoder<IllusionTowerRewardNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(IllusionTowerRewardNotification message) =>
        OnePayload(GameProtocol.IllusionTowerRewardType,
            IllusionTowerRewardPayloadCodec.Encode(message.Experience, message.Slots));
}
