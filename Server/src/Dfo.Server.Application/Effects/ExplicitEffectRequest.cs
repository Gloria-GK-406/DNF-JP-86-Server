using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal sealed class ExplicitEffectRequest(EffectOperationEntry entry, SelectedSessionRecipient recipient,
    ReplayProjectionNeeds minimum, SelectedSessionSafety safety, bool replay = false)
{
    internal CommandRequestContext Request { private get; set; } = null!;
    internal EffectOperationEntry Entry { get; } = entry;
    internal SelectedSessionRecipient Recipient { get; } = recipient;
    internal bool IsReplay { get; } = replay;
    private readonly EffectFootprintProjection projection = new(minimum);
    private bool mainClaimed, mainActive, transactionStarted, frozen, settled, replayCommitted;
    internal void DemandRecipient() => safety.DemandCurrent(Recipient);
    internal IEffectFootprintRecorder BeginMain(IReadOnlyList<SelectedSessionRecipient> participants, bool readOnly)
    {
        if (readOnly || IsReplay || mainClaimed)
            throw new InvalidOperationException("An effect request allows exactly one writable Main settlement.");
        mainClaimed = true;
        if (!participants.Any(p => p.Session == Recipient.Session && ReferenceEquals(p.Connection, Recipient.Connection)))
            throw new InvalidOperationException("The Main participants omit the effect reporter.");
        mainActive = true;
        return new ExplicitEffectFootprint(this);
    }
    internal void TransactionStarted() => transactionStarted = true;
    internal void Freeze()
    {
        if (!mainActive || !transactionStarted) throw new InvalidOperationException("No active Main transaction.");
        Entry.Projection = projection.Freeze(); frozen = true;
    }
    internal void Settled()
    {
        if (!frozen || !transactionStarted || Entry.Projection is null)
            throw new InvalidOperationException("Main has no frozen transaction projection.");
        Entry.State = EffectOperationState.Committed; settled = true;
    }
    internal void EndMain() => mainActive = false;
    internal void Failed(bool rollback, bool uncertain)
    {
        if (Entry.State == EffectOperationState.Committed) return;
        Entry.State = uncertain ? EffectOperationState.Blocked : rollback ? EffectOperationState.ConfirmedRollback : EffectOperationState.Rejected;
    }
    internal void DemandFinished()
    {
        if (!IsReplay && mainClaimed && Entry.State is not (EffectOperationState.Committed or EffectOperationState.ConfirmedRollback))
            throw new InvalidOperationException("The selected effect request did not complete its Main settlement.");
    }
    internal void ReplayCommitted() { if (IsReplay) replayCommitted = true; }
    internal void DemandRegistration(CommandRegistrationExit exit, NetworkConnectionContext connection)
    {
        if (!ReferenceEquals(connection, Recipient.Connection)
            || Entry.State != EffectOperationState.Committed || mainActive
            || !(exit switch
            {
                CommandRegistrationExit.SettledRequiredExplicit => settled && !IsReplay,
                CommandRegistrationExit.CommittedReplayCurrentStateBatch => IsReplay && replayCommitted,
                CommandRegistrationExit.SettledInitialBatch => settled && !IsReplay,
                _ => false,
            }))
            throw new InvalidOperationException("The registration exit requires its exact committed effect stage and connection.");
    }
    private void Demand(SelectedSessionIdentity owner)
    {
        Request.DemandCurrent();
        if (!mainActive || !transactionStarted || frozen || owner != Recipient.Session)
            Reject("The footprint requires the live owner and Main transaction.");
    }
    private void Reject(string message)
    {
        var error = new InvalidOperationException(message);
        Request.Failed(error);
        throw error;
    }
    private sealed class ExplicitEffectFootprint(ExplicitEffectRequest state) : IEffectFootprintRecorder
    {
        public void DemandEffects(EffectFootprintOwner owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            state.Demand(owner.Session ?? throw new InvalidOperationException("The footprint requires its selected owner."));
            if (owner.AccountId != state.Recipient.Session.AccountId || owner.CharacterId != state.Recipient.Session.CharacterId)
                state.Reject("The footprint owner differs from its selected session.");
        }
        public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied)
        { ArgumentNullException.ThrowIfNull(applied); DemandEffects(owner); state.projection.RecordEffects(applied); }
        public void DemandActive(SelectedSessionIdentity owner) { ArgumentNullException.ThrowIfNull(owner); state.Demand(owner); }
        public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots)
        { ArgumentNullException.ThrowIfNull(slots); DemandActive(owner); state.projection.Needs |= ReplayProjectionNeeds.Inventory; foreach (var slot in slots) state.projection.Slots.Add(slot); }
        public void RecordRentalState(SelectedSessionIdentity owner)
        { DemandActive(owner); state.projection.Needs |= ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Rental; }
    }
}
