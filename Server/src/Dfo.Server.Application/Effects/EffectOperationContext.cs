using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal enum EffectOperationState { Pending, Committed, ConfirmedRollback, Rejected, Blocked }

internal sealed class EffectOperationEntry(SelectedSessionIdentity session, long ordinal,
    byte selector, ushort type, ushort sequence, string hash, int length)
{
    public SelectedSessionIdentity Session { get; } = session;
    public long Ordinal { get; } = ordinal;
    public byte Selector { get; } = selector;
    public ushort Type { get; } = type;
    public ushort Sequence { get; } = sequence;
    public string Hash { get; } = hash;
    public int Length { get; } = length;
    public EffectOperationState State { get; set; }
    public ReplayProjectionRequest? Projection { get; set; }
}

/// <summary>The synchronous flow frame connects the one primary command to its preallocated ledger entry.</summary>
internal sealed class EffectOperationContext
{
    private readonly AsyncLocal<Frame?> current = new();
    private readonly AsyncLocal<Execution?> execution = new();
    private readonly AsyncLocal<Rejection?> rejection = new();
    private Frame? Current
    {
        get
        {
            if (rejection.Value is { Closed: true }) throw new InvalidOperationException("Inherited unselected operation has already closed.");
            if (current.Value is { Closed: true }) throw new InvalidOperationException("Inherited network operation has already closed.");
            if (execution.Value is { Closed: true }) throw new InvalidOperationException("Inherited command execution has already closed.");
            return current.Value;
        }
    }
    public IDisposable Enter(EffectOperationEntry entry, SelectedSessionRecipient recipient, ReplayProjectionNeeds minimum)
    {
        var previous = current.Value;
        var frame = new Frame(entry, recipient, minimum);
        current.Value = frame;
        return new Scope(() => { frame.Closed = true; current.Value = previous; });
    }
    public IDisposable EnterWithoutSelectedSession()
    {
        var previous = rejection.Value; var frame = new Rejection(); rejection.Value = frame;
        return new Scope(() => { frame.Closed = true; rejection.Value = previous; });
    }
    public IDisposable BeginMain(CommandExecutionContext commandExecution, ICommand command)
    {
        var frame = Current;
        if (rejection.Value is not null && command is IRequestSettlementCommand)
            throw new InvalidOperationException("An operation received without a selected session cannot settle a primary command.");
        if (frame is null) return new Scope(() => { });
        var isMain = command is IRequestSettlementCommand;
        if (command is IRequestSettlementCommand main)
        {
            if (main.Session != frame.Recipient.Session)
                throw new InvalidOperationException("Primary command differs from the operation's captured selected session.");
            lock (frame)
            {
                if (frame.MainExecutionId is not null)
                    throw new InvalidOperationException("A network effect request may execute only one primary settlement.");
                frame.MainExecutionId = commandExecution.ExecutionId;
            }
        }
        var previous = execution.Value;
        var active = new Execution(commandExecution.ExecutionId, isMain);
        execution.Value = active;
        return new Scope(() => { active.Closed = true; execution.Value = previous; });
    }
    private Frame? For(CommandExecutionContext commandExecution, ICommand command)
    {
        var frame = Current;
        return command is IRequestSettlementCommand && frame is not null
            && execution.Value is { IsMain: true, Closed: false } active && active.Id == commandExecution.ExecutionId ? frame : null;
    }
    public void DemandRecipient(CommandExecutionContext execution, ICommand command, SelectedSessionSafety safety)
    { if (For(execution, command) is { } frame) safety.DemandCurrent(frame.Recipient); }
    public void TransactionStarted(CommandExecutionContext execution, ICommand command)
    { if (For(execution, command) is { } frame) frame.TransactionStarted = true; }
    public void Freeze(CommandExecutionContext execution, ICommand command)
    {
        if (For(execution, command) is not { } frame) return;
        var projection = new ReplayProjectionRequest(frame.Needs, frame.Slots.ToImmutableArray()) { TouchedTownAreas = frame.Areas.ToImmutableArray() };
        projection.Validate();
        frame.Entry.Projection = projection;
        frame.Frozen = true;
    }
    // Settlement bookkeeping can still fence an expired inherited flow; it never grants execution rights.
    private Frame? ForSettlement(CommandExecutionContext commandExecution, ICommand command) =>
        command is IRequestSettlementCommand && current.Value is { } frame
            && execution.Value is { IsMain: true } active && active.Id == commandExecution.ExecutionId ? frame : null;
    public void RollbackConfirmed(CommandExecutionContext execution, ICommand command)
    { if (ForSettlement(execution, command) is { } frame) frame.RollbackConfirmed = true; }
    public void Settled(CommandExecutionContext execution, ICommand command)
    {
        if (For(execution, command) is not { } frame) return;
        if (!frame.TransactionStarted || !frame.Frozen || frame.Entry.Projection is null)
            throw new InvalidOperationException("A primary settlement returned without a prepared transaction projection.");
        frame.Entry.State = EffectOperationState.Committed;
    }
    public void Failed(CommandExecutionContext execution, ICommand command, bool uncertain)
    {
        if (ForSettlement(execution, command) is not { } frame) return;
        frame.Entry.State = uncertain ? EffectOperationState.Blocked
            : frame.RollbackConfirmed ? EffectOperationState.ConfirmedRollback : EffectOperationState.Rejected;
    }
    public void Record(EffectExecutionContext owner, IReadOnlyList<AppliedEffect> applied)
    {
        var frame = Current;
        if (rejection.Value is not null) throw new InvalidOperationException("An unselected network operation cannot apply effects.");
        if (frame is null) return;
        if (execution.Value is not { IsMain: true, Closed: false })
            throw new InvalidOperationException("Network effects must belong to the active primary command execution.");
        if (owner.Session != frame.Recipient.Session || owner.AccountId != frame.Recipient.Session.AccountId
            || owner.CharacterId != frame.Recipient.Session.CharacterId)
            throw new InvalidOperationException("Effect footprint differs from the captured primary settlement owner.");
        if (frame.Frozen) throw new InvalidOperationException("Effects cannot extend a frozen operation projection.");
        foreach (var item in applied)
        {
            switch (item.Effect, item.Change)
            {
                case (ConsumeFromContainerEffect, ContainerConsumptionChange change): Physical(change.Slot); break;
                case (SetEquipmentUpgradeEffect or SetEquipmentEnchantmentEffect or SetEquipmentDimensionalEffect
                    or SetEquipmentMagicSealEffect or SetEquipmentForgingEffect or DestroyEquipmentEffect, EquipmentEffectChange change):
                    Physical(change.Before.Slot); break;
                case (GrantStackableEffect, StackableGrantEffectChange change):
                    frame.Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var addition in change.Additions) Physical(addition.Slot);
                    foreach (var slot in change.Slots) Physical(slot.Slot);
                    break;
                case (GrantPreparedItemEffect grant, PreparedItemGrantChange change):
                    frame.Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var slot in change.Slots) Physical(slot);
                    if (grant.Grant.Item.Placement.ItemKind == 5) frame.Needs |= ReplayProjectionNeeds.Pets;
                    break;
                case (GrantBasicCharacterItemEffect grant, BasicCharacterItemChange change):
                    frame.Needs |= ReplayProjectionNeeds.Inventory;
                    foreach (var addition in change.Additions) Physical(addition.Slot);
                    if (grant.Placement.ItemKind == 5) frame.Needs |= ReplayProjectionNeeds.Pets;
                    break;
                case (SpendGoldEffect, GoldEffectChange):
                case (SetQuestRewardGoldEffect, QuestRewardGoldChange):
                case (GrantOpeningVirtualEffect, OpeningVirtualChange): frame.Needs |= ReplayProjectionNeeds.Inventory; break;
                case (MoveToTownEffect move, TownPositionChange):
                    frame.Needs |= ReplayProjectionNeeds.TownLocation;
                    frame.Areas.Add(move.SourceArea);
                    frame.Areas.Add(new(move.ChannelId, move.Destination.TownId, move.Destination.AreaId));
                    break;
                case (SetFatigueStateEffect, FatigueStateChange): frame.Needs |= ReplayProjectionNeeds.Fatigue; break;
                case (SetItemUsageStateEffect, ItemUsageStateChange): frame.Needs |= ReplayProjectionNeeds.ItemUsage; break;
                case (SetCarriedQuestEffect or RemoveTrackedQuestEffect or SetClearedQuestEffect, QuestRowChange): frame.Needs |= ReplayProjectionNeeds.Quests; break;
                case (SetLearnedSkillEffect, LearnedSkillChange): frame.Needs |= ReplayProjectionNeeds.Skills; break;
                case (RenamePetEffect, PetNameChange):
                case (AddPetStomachEffect, PetStomachChange): frame.Needs |= ReplayProjectionNeeds.Pets; break;
                case (CharacterExperienceEffect, CharacterExperienceChange): frame.Needs |= ReplayProjectionNeeds.Experience; break;
                case (SetCharacterGrowTypeEffect, CharacterGrowTypeChange):
                case (MaterializeSkillBuildsEffect, SkillBuildsMaterializedChange):
                    frame.Needs |= ReplayProjectionNeeds.CharacterAppearance | ReplayProjectionNeeds.Skills; break;
                case (SetOpeningPremiumEffect, OpeningPremiumChange):
                case (SetDoublePotUsageEffect, DoublePotUsageChange): frame.Needs |= ReplayProjectionNeeds.Premium; break;
                // These runtime changes have no safe absolute wire representation in the current protocol.
                case (StartItemCooldownEffect, ItemCooldownChange):
                case (StartSpiritPotionEffect, SpiritPotionChange):
                case (SetSeriaLuckEffect, SeriaLuckChange): break;
                default: throw new NotSupportedException($"No current-state footprint for {item.Effect.GetType().Name}/{item.Change.GetType().Name}.");
            }
        }
        void Physical(BusinessSlot slot) { frame.Needs |= ReplayProjectionNeeds.Inventory; frame.Slots.Add(slot); }
    }
    private sealed class Frame(EffectOperationEntry entry, SelectedSessionRecipient recipient, ReplayProjectionNeeds minimum)
    {
        public EffectOperationEntry Entry { get; } = entry;
        public SelectedSessionRecipient Recipient { get; } = recipient;
        public ReplayProjectionNeeds Needs { get; set; } = minimum;
        public HashSet<BusinessSlot> Slots { get; } = [];
        public HashSet<TownAreaKey> Areas { get; } = [];
        public Guid? MainExecutionId { get; set; }
        public bool TransactionStarted { get; set; }
        public bool Frozen { get; set; }
        public bool RollbackConfirmed { get; set; }
        public bool Closed { get; set; }
    }
    private sealed class Rejection { public bool Closed { get; set; } }
    private sealed class Execution(Guid id, bool isMain)
    {
        public Guid Id { get; } = id;
        public bool IsMain { get; } = isMain;
        public bool Closed { get; set; }
    }
    private sealed class Scope(Action dispose) : IDisposable
    { private Action? action = dispose; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
}
