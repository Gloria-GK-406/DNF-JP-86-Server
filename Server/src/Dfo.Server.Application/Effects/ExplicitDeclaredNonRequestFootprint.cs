using System.Collections.Frozen;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal sealed class ExplicitDeclaredNonRequestFootprint : IEffectFootprintRecorder
{
    private readonly CommandRequestContext request;
    private readonly DomainTransaction transaction;
    private readonly FrozenSet<SelectedSessionIdentity> owners;
    private bool active = true;

    internal ExplicitDeclaredNonRequestFootprint(CommandRequestContext request,
        IReadOnlyList<SelectedSessionIdentity> owners, DomainTransaction transaction)
    {
        if (request is not (CommandRequestContext.ProtocolNonEffect or CommandRequestContext.TrustedInternal))
            throw new InvalidOperationException("Declared owners require a non-effect protocol or trusted internal request.");
        this.request = request;
        this.transaction = transaction;
        this.owners = owners.ToFrozenSet();
    }

    internal void Close() => active = false;

    public void DemandActive(SelectedSessionIdentity owner) => Guard(() =>
    {
        if (owner is null || !owners.Contains(owner))
            throw new InvalidOperationException("The effect owner is outside this work unit's declared exact sessions.");
    });

    public void DemandEffects(EffectFootprintOwner owner) => Guard(() =>
    {
        if (owner?.Session is not { } session || session.AccountId != owner.AccountId || session.CharacterId != owner.CharacterId
            || !owners.Contains(session))
            throw new InvalidOperationException("The effect owner differs from this work unit's declared exact sessions.");
    });

    public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied) => Guard(() =>
    {
        DemandEffects(owner);
        ArgumentNullException.ThrowIfNull(applied);
    });
    public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots) => Guard(() =>
    {
        DemandActive(owner);
        ArgumentNullException.ThrowIfNull(slots);
    });
    public void RecordRentalState(SelectedSessionIdentity owner) => DemandActive(owner);

    private void Guard(Action validate)
    {
        try
        {
            request.DemandCurrent();
            if (!active || transaction.IsSettled || transaction.IsReadOnly)
                throw new InvalidOperationException("The declared footprint requires its active writable action.");
            validate();
        }
        catch (Exception error)
        {
            request.Failed(error);
            GameplayJob.RecordProgramError(error);
            throw;
        }
    }
}
