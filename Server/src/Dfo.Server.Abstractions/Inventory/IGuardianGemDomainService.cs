using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public enum GuardianGemRejection { None, InvalidTarget, InvalidGem }

public sealed record GuardianGemResult(GuardianGemRejection Rejection,
    ItemSnapshot? Medal = null, ItemSnapshot? RemainingGem = null);

public interface IGuardianGemDomainService
{
    ValueTask<GuardianGemResult> InstallAsync(SelectedSessionIdentity actor, BusinessSlot source,
        int expectedMedal, int expectedGem, byte socket, CancellationToken ct);
}
