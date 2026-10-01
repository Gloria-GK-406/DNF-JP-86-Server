using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

/// <summary>Owns the transitions of one pet. Reads always include this transaction's latest writes.</summary>
internal sealed class PetInstance(int itemUid, CreatureInstanceContext context) : IPetInstance
{
    public CharacterId Owner => context.Owner;
    public int ItemUid => itemUid;
    internal CreatureTransformationReceipt? BirthReceipt { get; init; }

    public async ValueTask<CreatureState> ReadAsync(CancellationToken ct)
    {
        var state = await context.ReadAsync(ItemUid, ct).ConfigureAwait(false);
        if (state is null || await context.Rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false)
            is not { IsEgg: false })
            throw new InvalidOperationException("This identity is no longer a pet owned by this character.");
        return state;
    }

    public async ValueTask<CreatureRuntimeChange> AddStomachAsync(int amount, CancellationToken ct)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        await context.SynchronizeAsync(ct).ConfigureAwait(false);
        var before = await ReadAsync(ct).ConfigureAwait(false);
        var value = CreatureRules.AddStomach(before.ExactStomach, amount);
        var after = before with { Stomach = (int)value, ExactStomach = value };
        context.Stage(after);
        var change = new CreatureRuntimeChange(before, after,
            await context.CurrentUidAsync(ct).ConfigureAwait(false) == ItemUid);
        context.Notify(change);
        return change;
    }

    public async ValueTask<CreatureRuntimeChange?> AddExpAsync(int amount, CancellationToken ct)
    {
        await context.SynchronizeAsync(ct).ConfigureAwait(false);
        return await AddExpCoreAsync(amount, ct).ConfigureAwait(false);
    }

    // The room-award loop has already synchronized the clock and must not enter it recursively.
    internal async ValueTask<CreatureRuntimeChange?> AddExpCoreAsync(int amount, CancellationToken ct)
    {
        var before = await ReadAsync(ct).ConfigureAwait(false);
        if (before.Stomach <= 0 || before.ModeFlag != 0 || amount <= 0) return null;
        var rule = await context.Rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (rule is null || rule.IsEgg || rule.HungerEnabled == false) return null;
        var equipment = context.Inventory?.FindContainer(BusinessInventorySpace.WornCreatureEquipment);
        if (equipment is not null)
        {
            var bonuses = new List<float>();
            foreach (var item in await equipment.ReadAllAsync(ct).ConfigureAwait(false))
                if (await context.Rules.GetArtifactExperienceAsync(item.ItemId, ct).ConfigureAwait(false) is { } artifact
                    && before.Level >= artifact.RequiredCreatureLevel)
                    bonuses.Add(artifact.BonusPercent);
            amount = CreatureRules.CalculateExperienceGain(amount, bonuses.Take(3).ToArray());
        }
        var growth = CreatureRules.GainExperience(rule, before.Experience, amount);
        var after = before with { Experience = growth.Experience, Level = growth.Level };
        if (before.Experience == after.Experience && before.Level == after.Level) return null;
        context.Stage(after);
        var equipped = await context.CurrentUidAsync(ct).ConfigureAwait(false) == ItemUid;
        var transformation = equipped && growth.LevelChanged
            ? await EvolveAsync(ct).ConfigureAwait(false) : null;
        var change = new CreatureRuntimeChange(before, transformation?.After ?? after, equipped)
        {
            Transformation = transformation
        };
        context.Notify(change);
        return change;
    }

    public async ValueTask<CreatureState?> RenameAsync(byte[] nameBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        context.EnsureActive();
        if (nameBytes.Length > 12 || nameBytes.Any(value => value is 0 or < 0x20 or 0x7F)) return null;
        var before = await ReadAsync(ct).ConfigureAwait(false);
        var location = await context.LocateAsync(ItemUid, ct).ConfigureAwait(false);
        if (location is null || !context.IsLive(location, before)) return null;
        var after = before with { NameBytes = [.. nameBytes] };
        context.Stage(after);
        return after;
    }

    public async ValueTask<CreatureTransformationReceipt?> EvolveAsync(CancellationToken ct)
    {
        var before = await ReadAsync(ct).ConfigureAwait(false);
        var location = await context.LocateAsync(ItemUid, ct).ConfigureAwait(false);
        if (location is null || location.Slot.Space != BusinessInventorySpace.WornCreature
            || location.Slot.Index != 0 || !context.IsLive(location, before)) return null;
        var source = await context.Rules.GetAsync(before.ItemTemplateId, ct).ConfigureAwait(false);
        if (source is null || source.OutputTemplateId is not > 0 || source.Evolutions.Count != 1) return null;
        var step = source.Evolutions[0];
        if (step.RequiredLevel is not > 0 || before.Level < step.RequiredLevel || step.RequiredQuestId is > 0) return null;
        var target = await context.Rules.GetAsync(source.OutputTemplateId.Value, ct).ConfigureAwait(false);
        if (target is null || target.IsEgg || target.CreatureId != step.EvolvesIntoCreatureId) return null;
        var after = before with { ItemTemplateId = target.TemplateId };
        var core = location.Core with { ItemId = target.TemplateId };
        context.Stage(after);
        await location.Container.PutAsync(location.Slot.Index, core, ct).ConfigureAwait(false);
        return new(before, after, location.Slot, core) { CreatureId = target.CreatureId };
    }

    internal async ValueTask<CreatureState> AdvanceStomachAsync(CreatureState state, double oldValue,
        TimeSpan elapsed, bool consuming, bool revive, bool forceWrite, CancellationToken ct)
    {
        var rule = await context.Rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
        var value = AdvanceBalance(oldValue, elapsed, consuming, rule?.HungerEnabled ?? true);
        if (revive && value <= 0) value = 1;
        var after = state with { Stomach = (int)value, ExactStomach = value };
        if (state.Stomach != after.Stomach || (oldValue > 0) != (value > 0) || forceWrite)
            context.Stage(after);
        return after;
    }

    internal static double AdvanceBalance(double value, TimeSpan elapsed, bool combat, bool hungerEnabled = true) =>
        CreatureRules.AdvanceStomach(value, elapsed, combat, hungerEnabled);
}
