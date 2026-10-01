using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Effects;

internal sealed class TownPositionEffectHandler(ICharacterPositionDomainService positions,
    ISelectedSessionPositionPersistence persistence, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions, ITownAreaDomainService areas) : EffectHandler<MoveToTownEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, MoveToTownEffect effect, CancellationToken ct)
    {
        if (context.Session is not { } session || sessions.Find(session.AccountId) != session
            || positions.Find(context.CharacterId) != effect.Expected
            || areas.Find(context.CharacterId) is not { } member || !session.Owns(member) || member.Area != effect.SourceArea)
            throw new InvalidOperationException("Town movement target no longer satisfies its plan.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, MoveToTownEffect effect, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        if (effect.PersistsPosition)
            await persistence.PersistAsync(context.AccountId, context.CharacterId, effect.Destination, ct).ConfigureAwait(false);
        transactions.Current!.Enlist(new object(), () => new Commit(positions, sessions, areas, context, effect));
        return new TownPositionChange(effect.Expected, effect.Destination);
    }
    private sealed class Commit(ICharacterPositionDomainService positions, ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
        EffectExecutionContext context, MoveToTownEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (sessions.Find(context.AccountId) == context.Session)
            {
                areas.TransferExact(context.CharacterId, context.Session!.TownAreaLease(),
                    new(effect.ChannelId, effect.Destination.TownId, effect.Destination.AreaId), effect.Destination, false);
                positions.CrossInto(context.Session!, effect.Destination.TownId, effect.Destination.AreaId,
                    effect.Destination.X, effect.Destination.Y);
                positions.StepTo(context.Session!, effect.Destination.X, effect.Destination.Y, effect.Destination.Direction);
            }
            return ValueTask.CompletedTask;
        }
    }
}
