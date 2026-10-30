using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.World;

internal sealed class DungeonRevivalDomainService(ISelectedSessionDomainService sessions,
    ICharacterContainerDomainService containers, IDungeonRunParticipantDomainService runs,
    ITournamentDungeonDomainService tournaments, IAltarDungeonDomainService altars, IDarkElfRuinsDomainService ruins,
    Dfo.Server.Abstractions.Quests.IQuestProgressDomainService quests) : IDungeonRevivalDomainService
{
    public async ValueTask<DungeonRevivalResult> ReviveAsync(SelectedSessionIdentity payer,
        CharacterId target, DungeonRunId runId, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(payer)) return new(false);
        var policy = runs.Find(target)?.Layout.Mode.Capabilities.Revival ?? DungeonRevivalPolicy.Ordinary;
        var permitted = policy switch
        {
            DungeonRevivalPolicy.Ordinary => true,
            DungeonRevivalPolicy.Forbidden => false,
            DungeonRevivalPolicy.TournamentLife => tournaments.CanRevive(target, runId),
            DungeonRevivalPolicy.AltarLife => altars.CanRevive(target, runId),
            DungeonRevivalPolicy.DarkElfRuinsLife => ruins.CanRevive(payer.CharacterId, runId),
        };
        if (!permitted) return new(false);
        var bag = containers.FindContainer(payer.CharacterId, InventoryContainerType.Main);
        var coin = bag is null ? null : await bag.GetAsync(1, cancellationToken).ConfigureAwait(false);
        if (coin is null || coin.Snapshot.ItemId != 1 || coin.Count <= 0) return new(false);
        if (!runs.StageRevival(payer.CharacterId, target, runId)) return new(false);
        if (policy == DungeonRevivalPolicy.TournamentLife && !tournaments.StageRevival(target, runId))
            throw new InvalidOperationException("Tournament revival changed after admission; roll back the complete revival.");
        if (policy == DungeonRevivalPolicy.AltarLife && !altars.StageRevival(target, runId))
            throw new InvalidOperationException("Altar revival changed after admission.");
        var remaining = (coin.Snapshot with { Count = coin.Count - 1 }).ToCore();
        // Keep the virtual counter at zero; the transactional item store commits before life state.
        await bag!.UpdateAsync(coin, current => current with { Count = current.Count - 1 }, cancellationToken).ConfigureAwait(false);
        if (runs.Find(target) is { Layout.Mode: DungeonRunMode.Mirror, Participant: { } participant })
            await quests.ReportAsync(participant.AccountId, target,
                [new Dfo.Server.Abstractions.Quests.QuestCharacterRevived(runId, target)], cancellationToken).ConfigureAwait(false);
        return new(true, remaining);
    }
}
