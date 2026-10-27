using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Data.World;

/// <summary>Transport-bound ownership of one selected character generation.</summary>
public sealed record SelectedSessionIdentity(
    AccountId AccountId,
    CharacterId CharacterId,
    NetworkConnectionId ConnectionId,
    // Generation observed before the session enters the world. Entering advances the
    // online user's generation; this one stays the one the selection was made under.
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

/// <summary>How far a selected session has got.</summary>
public enum SelectedSessionPhase
{
    /// <summary>Selected and still loading: current, but not yet standing anywhere.</summary>
    Selected,

    /// <summary>Published into the world.</summary>
    InWorld,
}

/// <summary>Where one exact selected session stands, read in one moment.</summary>
/// <param name="Fenced">
/// A settlement it ran has an unknown outcome; nothing more may be done in its name.
/// </param>
/// <param name="ConnectionRevoked">
/// Its connection is no longer registered. The session stays current until its teardown has
/// released what it holds elsewhere, but it can no longer act.
/// </param>
public sealed record SelectedSessionStanding(
    SelectedSessionPhase Phase,
    bool Fenced,
    bool ConnectionRevoked);
