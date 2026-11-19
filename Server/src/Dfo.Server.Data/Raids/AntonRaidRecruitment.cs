using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Raids;

public sealed record AntonRaidMember(
    SelectedSessionIdentity Session, ReadOnlyMemory<byte> Name, byte Job, byte GrowthType, byte Squad);

public sealed record AntonRaidRecruitmentSnapshot(
    uint Number, ReadOnlyMemory<byte> Title, IReadOnlyList<AntonRaidMember> Members)
{
    public AntonRaidMember Leader => Members[0];
}

public sealed record AntonRaidRecruitmentChange(
    uint Number,
    AntonRaidRecruitmentSnapshot? Raid,
    IReadOnlyList<AntonRaidRecruitmentSnapshot> Directory,
    IReadOnlyList<SelectedSessionIdentity> Audience);

public sealed record AntonRaidRecruitmentResult(
    bool Succeeded, uint Number, IReadOnlyList<AntonRaidRecruitmentChange> Changes, PartyNotificationPlan Party)
{
    public static AntonRaidRecruitmentResult Refused { get; } = new(false, 0, [], PartyNotificationPlan.Empty);
    public static AntonRaidRecruitmentResult Declined { get; } = new(true, 0, [], PartyNotificationPlan.Empty);
}

public sealed record AntonRaidApproval(
    SelectedSessionIdentity Sender,
    SelectedSessionIdentity Recipient,
    AntonRaidMember Applicant,
    SelectedSessionIdentity Leader,
    uint Number,
    DateTimeOffset ExpiresAt);
