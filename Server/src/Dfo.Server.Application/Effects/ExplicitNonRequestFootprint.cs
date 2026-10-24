using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

/// <summary>Only one explicitly declared protocol ordinary-write action owns this capability.</summary>
internal sealed class ExplicitNonRequestFootprint(CommandRequestContext.ProtocolNonEffect request,
    SelectedSessionRecipient actor, DomainTransaction transaction, SelectedSessionSafety safety) : IEffectFootprintRecorder
{
    private bool active = true;
    internal void Close() => active = false;
    public void DemandActive(SelectedSessionIdentity owner)
    {
        DemandRequest();
        if (!active || transaction.IsSettled || transaction.IsReadOnly || owner != actor.Session
            || !ReferenceEquals(actor.Connection, request.Dispatch.Connection)
            || owner.ConnectionId != request.Dispatch.Connection.ConnectionId)
            Reject("The NonRequest footprint requires its exact active actor and write unit.");
        try { safety.DemandCurrent(actor); }
        catch (Exception error) { request.Failed(error); throw; }
    }
    public void DemandEffects(EffectFootprintOwner owner)
    {
        DemandRequest();
        if (owner?.Session is null)
        {
            var error = new InvalidOperationException("The protocol footprint requires its selected actor.");
            request.Failed(error);
            throw error;
        }
        DemandActive(owner.Session);
        if (owner.AccountId != actor.Session.AccountId || owner.CharacterId != actor.Session.CharacterId)
            Reject("The NonRequest footprint owner differs from its actor.");
    }
    public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied)
    { DemandEffects(owner); ArgumentNullException.ThrowIfNull(applied); }
    public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots)
    { DemandActive(owner); ArgumentNullException.ThrowIfNull(slots); }
    public void RecordRentalState(SelectedSessionIdentity owner) => DemandActive(owner);
    private void DemandRequest()
    {
        try { request.DemandCurrent(); }
        catch (Exception error) { request.Failed(error); throw; }
    }
    private void Reject(string message)
    {
        var error = new InvalidOperationException(message);
        request.Failed(error);
        throw error;
    }
}
