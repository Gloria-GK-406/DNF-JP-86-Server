using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;

namespace Dfo.Server.Application.Achievements;

internal sealed class AchievementCharacterFactsDomainService(ICharacterDomainService characters,
    ICharacterTable owners, ICharacterQuestLogDomainService quests, ICharacterConditionTable conditions,
    IItemDomainStore items, IDomainTransactionAccessor transactions) : IAchievementCharacterFactsDomainService
{
    public async ValueTask<AchievementCharacterFacts> ReadAsync(CharacterId character, CancellationToken ct)
    {
        var progress = await characters.Get(character).ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Achievement character is unavailable.");
        var account = await owners.FindOwnerAsync(character, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Achievement character owner is unavailable.");
        var cleared = await quests.LogOf(account, character).ClearedAsync(ct).ConfigureAwait(false);
        var condition = await conditions.FindAsync(character, ct).ConfigureAwait(false);
        return new(progress.Level, cleared.Select(x => x.QuestId).ToHashSet())
        {
            Hardcore = condition is null ? null : condition.HardcoreMode != 0,
        };
    }
    public async ValueTask<AchievementCharacterFacts> ReadForUnlockAsync(CharacterId character, IReadOnlyList<AchievementDefinition> pending, CancellationToken ct)
    {
        var standing = await owners.FindStandingAsync(character, ct)
            ?? throw new KeyNotFoundException("Achievement character is unavailable.");
        var cleared = new HashSet<int>();
        if (pending.Any(x => x.Condition.Type is "clear quest" or "clearquest" || x.Prerequisites.Count > 0 || x.PrerequisiteGroups.Count > 0))
        {
            var account = await owners.FindOwnerAsync(character, ct) ?? throw new KeyNotFoundException("Achievement owner is unavailable.");
            cleared = (await quests.LogOf(account, character).ClearedAsync(ct)).Select(x => x.QuestId).ToHashSet();
        }
        var mode = pending.Any(x => x.RequiresHardcore is not null)
            ? (await conditions.FindAsync(character, ct))?.HardcoreMode : null;
        var counts = new Dictionary<int, long>();
        int? avatars = null;
        var creatures = new List<int>();
        var equipped = new List<AchievementEquippedItem>();
        var seeking = pending.Where(x => x.Condition.Type == "seeking").ToArray();
        var bagNeeded = seeking.Any(x => x.Condition.Values[0] > 0);
        var avatarsNeeded = seeking.Any(x => x.Condition.SubType != 25 && x.Condition.Values.FirstOrDefault() == -7);
        var creaturesNeeded = seeking.Any(x => x.Condition.SubType == 25);
        var equippedNeeded = pending.Any(x => x.Condition.Type == "equipped item");
        if (bagNeeded || avatarsNeeded || creaturesNeeded || equippedNeeded)
        {
            var session = items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Unlock checks require a transaction."));
            async ValueTask<IReadOnlyList<ItemReference>> Read(InventoryContainerType type) => await session.GetAllAsync(ContainerId.ForCharacter(character, type), ct);
            if (bagNeeded)
            {
                var account = await owners.FindOwnerAsync(character, ct) ?? throw new KeyNotFoundException("Achievement owner is unavailable.");
                var shared = await session.GetAllAsync(ContainerId.ForAccount(account, InventoryContainerType.SharedMaterial), ct);
                foreach (var item in (await Read(InventoryContainerType.Main)).Concat(shared))
                    counts[item.ItemId] = counts.GetValueOrDefault(item.ItemId) + item.Count;
            }
            if (avatarsNeeded) avatars = (await Read(InventoryContainerType.Avatar)).Count(x => x.Snapshot.Type.HasFlag(ItemTraits.Avatar));
            if (creaturesNeeded) creatures.AddRange((await Read(InventoryContainerType.Pet)).Where(x => x.Snapshot.Type == ItemTraits.Creature).Select(x => x.ItemId));
            if (avatarsNeeded || creaturesNeeded || equippedNeeded)
                foreach (var item in await Read(InventoryContainerType.Equipment))
                {
                    BusinessSlot slot;
                    try { slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Equipment, item.StorageSlotIndex)); }
                    catch (InventorySlotMappingException) { continue; }
                    if (avatarsNeeded && slot.Space == BusinessInventorySpace.WornAvatar) avatars++;
                    if (creaturesNeeded && slot.Space == BusinessInventorySpace.WornCreature) creatures.Add(item.ItemId);
                    if (equippedNeeded && slot.Space == BusinessInventorySpace.WornEquipment) equipped.Add(new(slot, item.ItemId));
                }
        }
        return new(standing.Level, cleared)
        {
            Career = CharacterCareer.FromPacked(checked((byte)standing.Job), standing.GrowType),
            Hardcore = mode is null ? null : mode != 0, ItemCounts = counts, AvatarCount = avatars,
            CreatureItems = creatures, EquippedItems = equipped,
        };
    }
}
