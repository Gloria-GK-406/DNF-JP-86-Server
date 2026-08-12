using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Data.World;

/// <summary>Transport-bound ownership of one selected character generation.</summary>
public sealed record SelectedSessionIdentity(
    AccountId AccountId,
    CharacterId CharacterId,
    NetworkConnectionId ConnectionId,
    // Generation observed before BindCharacter publishes the selected character. The
    // online-user aggregate advances independently after publication.
    long ConnectionGeneration,
    ushort WireUserId,
    int ListenerPort)
{
    public static SelectedSessionIdentity Create(
        AccountId accountId,
        CharacterId characterId,
        NetworkConnectionId connectionId,
        long connectionGeneration,
        int listenerPort)
    {
        if (connectionGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionGeneration));
        if (string.IsNullOrWhiteSpace(connectionId.Value))
            throw new ArgumentException("A selected session requires a connection.", nameof(connectionId));
        return new SelectedSessionIdentity(
            accountId, characterId, connectionId, connectionGeneration,
            ToWireUserId(characterId), listenerPort);
    }

    public static ushort ToWireUserId(CharacterId characterId)
    {
        if (characterId.Value is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(characterId),
                "The character ID cannot be represented losslessly as a wire user ID.");
        return checked((ushort)characterId.Value);
    }
}
