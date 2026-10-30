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
    public static IGameServerMessage Premium(AccountPremiumEntryState premium, DateTimeOffset now) => new PremiumGrantedMessage(premium, now);
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
        services.AddGameNotiMessageEncoder<PremiumGrantedMessage, PremiumGrantedEncoder>();
    }
}

internal sealed record PremiumGrantedMessage(AccountPremiumEntryState Premium, DateTimeOffset PreparedAt) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; internal ushort Type => 0x0042; }
internal sealed class PremiumGrantedEncoder : GameNotiMessageEncoder<PremiumGrantedMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PremiumGrantedMessage message)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)2); writer.Write(checked((byte)message.Premium.PremiumType));
        writer.Write(Math.Max(0, message.Premium.ExpiresAtUnixSeconds - message.PreparedAt.ToUnixTimeSeconds()));
        return OnePayload(message.Type, stream.ToArray());
    }
}

internal sealed record CeraBalanceUpdateMessage(AccountCeraBalances Balances) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; internal ushort Type => 0x0035; }
internal sealed record CreatureListUpdateMessage(IReadOnlyList<CharacterPetRuntimeState> Pets) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization; internal ushort Type => 0x0069; }
internal sealed class CeraBalanceUpdateEncoder : GameNotiMessageEncoder<CeraBalanceUpdateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CeraBalanceUpdateMessage message) =>
        OnePayload(message.Type, GameInstanceDelivery.EncodeWallet(message.Balances));
}
internal sealed class CreatureListUpdateEncoder : GameNotiMessageEncoder<CreatureListUpdateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureListUpdateMessage message) =>
        OnePayload(message.Type, GameInstanceDelivery.EncodeCreatures(message.Pets));
}
