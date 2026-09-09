using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game;

/// <summary>Wallet and creature projections shared by entry and subsequent purchases.</summary>
public static class GameInstanceDelivery
{
    public static IGameServerMessage Wallet(AccountCeraBalances balances) => new CeraBalanceUpdateMessage(balances);
    public static IGameServerMessage Creatures(IReadOnlyList<CharacterPetRuntimeState> pets) => new CreatureListUpdateMessage(pets);
    internal static byte[] EncodeWallet(AccountCeraBalances balances)
    {
        using var stream = new MemoryStream(13); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1); writer.Write(balances.Cera); writer.Write(balances.TokenCera); writer.Write(balances.HappyTokenCera);
        return stream.ToArray();
    }
    internal static byte[] EncodeCreatures(IReadOnlyList<CharacterPetRuntimeState> pets)
    {
        if (pets.Count > byte.MaxValue) throw new InvalidDataException("creature count cannot be represented as u8.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)pets.Count));
        foreach (var pet in pets)
        {
            writer.Write(checked((int)pet.PetKey)); writer.Write(checked((byte)pet.Field04));
            writer.Write(pet.ModeFlag); writer.Write(pet.ProgressValue);
            if (pet.ModeFlag == 1) { writer.Write(checked((byte)pet.Mode1Field0A)); writer.Write(checked((byte)pet.Mode1Field0B)); }
            writer.Write(checked((byte)pet.FieldAfterValue)); writer.Write(pet.CreatureText.Length); writer.Write(pet.CreatureText); writer.Write(pet.TailFlag);
        }
        return stream.ToArray();
    }
    public static void AddEncoders(IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<CeraBalanceUpdateMessage, CeraBalanceUpdateEncoder>();
        services.AddGameNotiMessageEncoder<CreatureListUpdateMessage, CreatureListUpdateEncoder>();
    }
}

internal sealed record CeraBalanceUpdateMessage(AccountCeraBalances Balances) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; }
internal sealed record CreatureListUpdateMessage(IReadOnlyList<CharacterPetRuntimeState> Pets) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; }
internal sealed class CeraBalanceUpdateEncoder : GameNotiMessageEncoder<CeraBalanceUpdateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CeraBalanceUpdateMessage message) =>
        OnePayload(0x0035, GameInstanceDelivery.EncodeWallet(message.Balances));
}
internal sealed class CreatureListUpdateEncoder : GameNotiMessageEncoder<CreatureListUpdateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureListUpdateMessage message) =>
        OnePayload(0x0069, GameInstanceDelivery.EncodeCreatures(message.Pets));
}
