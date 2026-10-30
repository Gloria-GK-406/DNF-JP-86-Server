using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestFactsDomainService(ICharacterContainerDomainService containers,
    ICharacterDomainService characters, ICharacterQuestLogDomainService logs,
    IQuestStateRuleService rules, ICharacterTable owners, ICreatureDomainStore creatures, AmbientStoreAccess stores,
    IItemDomainStore items, IGiantDragonCrystalExchangeDomainService crystalExchange,
    Dfo.Server.Abstractions.Marriage.IMarriageQuestDomainService marriage,
    Dfo.Server.Abstractions.World.ISouthernDaleQuestDomainService southern) : IQuestFactsDomainService
{
    public async ValueTask<QuestHoldingsFacts> HoldingsAsync(CharacterId character, CancellationToken ct)
    {
        return await QuestHoldingsReader.ReadAsync(containers, rules, character,
            await characters.Get(character).GoldAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    public async ValueTask<QuestHoldingsFacts> PersistentHoldingsAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) != account)
            throw new InvalidDataException("Quest holdings character ownership does not match.");
        var main = await stores.ReadAsync(items, rows => rows.GetAllAsync(
            ContainerId.ForCharacter(character, InventoryContainerType.Main), ct)).ConfigureAwait(false);
        var shared = await stores.ReadAsync(items, rows => rows.GetAllAsync(
            ContainerId.ForAccount(account, InventoryContainerType.SharedMaterial), ct)).ConfigureAwait(false);
        return rules.Holdings(main.Select(x => x.Snapshot), shared.Select(x => x.Snapshot),
            await characters.Get(character).GoldAsync(ct).ConfigureAwait(false));
    }

    public async ValueTask<WorldContentSubject> SubjectAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) != account)
            throw new InvalidDataException("Quest query character ownership does not match.");
        var domain = characters.Get(character);
        var identity = await domain.IdentityAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Quest query character identity is unavailable.");
        var progress = await domain.ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Quest query character progress is unavailable.");
        var log = logs.LogOf(account, character);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        var creature = await stores.ReadAsync(creatures, rows => rows.ReadCurrentAsync(character, ct)).ConfigureAwait(false);
        return new WorldContentSubject(progress.Level, identity.Job, identity.GrowType,
            cleared.Where(x => x.Mark != 0).ToDictionary(x => x.QuestId, x => (int)x.Mark),
            carried.Select(x => x.QuestId).ToArray(), [], [], creature?.ItemTemplateId ?? 0, creature?.Level ?? 0)
        { GiantDragonCrystalExchangeAvailable = await crystalExchange.CanAcceptAsync(character, ct).ConfigureAwait(false),
            EligibleWeddingQuestIds = await marriage.EligibleQuestIdsAsync(account, character, ct).ConfigureAwait(false),
            SouthernDaleQuestAvailable = await southern.CanAcceptAsync(character, ct).ConfigureAwait(false) };
    }
}
