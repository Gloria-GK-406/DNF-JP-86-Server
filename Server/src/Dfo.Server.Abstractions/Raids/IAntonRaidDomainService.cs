using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Raids;

/// <summary>Owns process-local Anton raid recruitment and its pending approvals.</summary>
public interface IAntonRaidDomainService
{
    int? RecruitmentChannel(SelectedSessionIdentity session);
    bool IsRecruitingMember(SelectedSessionIdentity session);
    IReadOnlyList<AntonRaidRecruitmentSnapshot> Directory(int channel);
    AntonRaidRecruitmentSnapshot? Find(int channel, uint number);
    uint? CheckApplicantTarget(SelectedSessionIdentity applicant, SelectedSessionIdentity target);
    AntonRaidRecruitmentResult Create(AntonRaidMember leader, ReadOnlyMemory<byte> title);
    AntonRaidApproval? Request(AntonRaidMember sender, AntonRaidMember recipient);
    AntonRaidRecruitmentResult Respond(SelectedSessionIdentity recipient, SelectedSessionIdentity sender, bool accepted);
    AntonRaidRecruitmentResult Assign(SelectedSessionIdentity sender, ushort target, uint squad);
    AntonRaidRecruitmentResult Leave(SelectedSessionIdentity sender, ushort target);
    AntonRaidRecruitmentChange? Disconnected(SelectedSessionIdentity session);
}
