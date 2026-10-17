using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Marriage;
public sealed record MarriageLovePointItemRule(ItemUseRule Use, int Amount, int Maximum);
public sealed record MarriageLovePointResult(int Status, int Points = 0, CharacterId? Partner = null)
{ public Dfo.Server.Abstractions.Inventory.SharedEffectSlotState? State { get; init; } }
public interface IMarriageLovePointRuleService
{ ValueTask<MarriageLovePointItemRule?> RuleForAsync(int itemId, CancellationToken ct); }
public interface IMarriageLovePointDomainService
{
    ValueTask<IReadOnlyList<AccountId>> AccountsAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<MarriageLovePointResult> UseAsync(SelectedSessionIdentity actor, BusinessSlot slot, IReadOnlyList<AccountId> accounts, CancellationToken ct);
}
