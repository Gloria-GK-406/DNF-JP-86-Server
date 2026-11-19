using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Raids;

namespace Dfo.Server.Application.World;

public sealed class DungeonEntryRoutingDomainService(
    ILostLandDungeonDomainService lostLand,
    IGuardianAltarDomainService guardian,
    IGiantDragonDomainService giantDragon,
    ICoupleDungeonDomainService couples,
    IGoldSilverHornDomainService goldSilverHorn,
    ISealedTreasureDomainService sealedTreasure,
    ISouthernDaleDomainService southern,
    IHerasDungeonDomainService heras,
    ITauKingdomDomainService tau,
    IAltarDungeonDomainService altars,
    ITournamentDungeonDomainService tournaments,
    ISaoTowerDomainService sao,
    IDespairTowerDomainService despair,
    IMirrorDungeonEntryDomainService mirrors,
    ICycleDungeonEntryDomainService cycles,
    IDungeonEntryDecisionDomainService decisions,
    IDimensionDungeonDomainService dimensions,
    IHellDungeonEntryDomainService hell, IDarkElfRuinsDomainService ruins,
    IAntonRaidDomainService? raids = null) : IDungeonEntryRoutingDomainService
{
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext entry, CancellationToken ct) =>
        entry.IsRaidChannel || entry.Facts.Intent.Kind == DungeonEntryIntentKind.AntonRaid
            || entry.DungeonId is >= 210 and <= 216 or >= 218 and <= 224
            ? raids?.EnterAsync(entry, ct) ?? ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite))
        : ruins.Supports(entry.DungeonId) ? ruins.EnterAsync(entry, ct)
        : lostLand.Supports(entry.DungeonId) ? lostLand.EnterAsync(entry, ct)
        : guardian.Supports(entry.DungeonId) ? guardian.EnterAsync(entry, ct)
        : giantDragon.Supports(entry.DungeonId) ? giantDragon.EnterAsync(entry, ct)
        : couples.Supports(entry.DungeonId) ? couples.EnterAsync(entry, ct)
        : goldSilverHorn.Supports(entry.DungeonId) ? goldSilverHorn.EnterAsync(entry, ct)
        : sealedTreasure.Supports(entry.DungeonId) ? sealedTreasure.EnterAsync(entry, ct)
        : southern.Supports(entry.DungeonId) ? southern.EnterAsync(entry, ct)
        : heras.Supports(entry.DungeonId) ? heras.EnterAsync(entry, ct)
        : tau.Supports(entry.DungeonId) ? tau.EnterAsync(entry, ct)
        : altars.Supports(entry.DungeonId) ? altars.EnterAsync(entry, ct)
        : tournaments.Supports(entry.DungeonId) ? tournaments.EnterAsync(entry, ct)
        : sao.Supports(entry.DungeonId) ? sao.EnterAsync(entry, ct)
        : despair.IsTower(entry.DungeonId) ? despair.EnterAsync(entry, ct)
        : entry.MirrorQuestId is not null ? mirrors.EnterAsync(entry, ct)
        : entry.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle ? cycles.EnterAsync(entry, ct)
        : decisions.ResolveEntryKind(entry) switch
        {
            DungeonEntryKind.Normal => dimensions.EnterAsync(entry, ct),
            DungeonEntryKind.ManualHell => hell.EnterManualAsync(entry, ct),
            DungeonEntryKind.RandomHell => hell.EnterRandomAsync(entry, ct),
        };
}
