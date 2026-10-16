using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Marriage;

public sealed record MarriageChurchLocation(byte TownId, byte AreaId, short X, short Y);
public sealed record MarriageChurchRoom(uint Token, Guid CeremonyId, int ChannelId,
    byte TownId, byte AreaId, DateTimeOffset PreparedAt, DateTimeOffset ReadyUntil);
public sealed record MarriageChurchSnapshot(MarriageChurchRoom Room, CharacterId Peer,
    byte[] ApplicantName, byte[] PartnerName, MarriageChurchLocation Location);
public sealed record MarriageChurchEntryResult(int Status, MarriageChurchSnapshot? Snapshot = null)
{
    public bool Succeeded => Status == 0 && Snapshot is not null;
}
