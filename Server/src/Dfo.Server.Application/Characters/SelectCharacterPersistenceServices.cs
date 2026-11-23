using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Brings a character's skills back within what their expired contracts allow.
/// </summary>
/// <remarks>
/// Reads, asks, writes. What comes down and to what is the rule service's answer; which rows to
/// read and in what order to act is this one's. Neither of them opens a connection.
/// </remarks>
internal sealed class ExpertSkillContractReconciliationService(
    IPremiumServiceDomainService premiums,
    ICharacterTable characters,
    ICharacterSkillTable skills,
    IExpertSkillContractRuleService rules) : IExpertSkillContractReconciliationRepository
{
    public async ValueTask<bool> ReconcileAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlySet<int> overSkillPremiumTypes,
        IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline,
        CancellationToken cancellationToken)
    {
        if (overSkillPremiumTypes.Count == 0)
        {
            return false;
        }

        var account = premiums.For(accountId);
        var held = await account.ReadAsync(cancellationToken).ConfigureAwait(false);
        var types = held.Where(row => overSkillPremiumTypes.Contains(row.PremiumType))
            .Select(row => row.PremiumType).Distinct().ToArray();
        if (types.Length == 0) return false;
        foreach (var type in types)
            if (await account.IsActiveAsync((PremiumServiceType)type, cancellationToken).ConfigureAwait(false))
                return false;

        if (await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false)
            != accountId)
        {
            throw new InvalidDataException("Expert-contract skill owner is unavailable.");
        }

        var learned = await skills.ReadLevelsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var changes = rules.CapsFor(
            [.. learned.Select(row => (row.PageIndex, row.Slot, row.SkillId, row.Level))],
            definitions,
            freeBaseline);
        foreach (var change in changes)
        {
            var touched = change.Level is null
                ? await skills.RemoveAsync(
                    characterId, change.Page, change.Slot, cancellationToken).ConfigureAwait(false)
                : await skills.SetLevelAsync(
                    characterId, change.Page, change.Slot, change.Level.Value, cancellationToken)
                    .ConfigureAwait(false);
            if (touched != 1)
            {
                throw new InvalidDataException("Expert-contract skill row changed concurrently.");
            }
        }

        return changes.Count > 0;
    }
}
