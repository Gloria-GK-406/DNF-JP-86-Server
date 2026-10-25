using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Networking;

/// <summary>The single generation stream shared by party mutation and town read repair.</summary>
public interface IPartyProjectionStateDomainService
{
    IPartyDeliveryStream StreamOf(long generation);
}

/// <summary>Live projection state, including tombstones; this is not a copied snapshot.</summary>
public interface IPartyDeliveryStream
{
    long LatestRevision { get; set; }
    ushort? CurrentPartyId { get; set; }
    HashSet<SelectedSessionIdentity> CurrentMembers { get; }
    HashSet<SelectedSessionIdentity> FullStateMembers { get; }
}
