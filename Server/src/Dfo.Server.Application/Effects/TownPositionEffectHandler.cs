using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Effects;

internal sealed class TownPositionEffectHandler(IOnlineCharacterRuntimeDomainService onlineCharacters,
    ISelectedSessionPositionPersistence persistence, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions) : EffectHandler<MoveToTownEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, MoveToTownEffect effect, CancellationToken ct)
    {
        if (context.Session is not { } session || !sessions.IsInWorld(session)
            || onlineCharacters.StandingOf(session) is not { } member
            || member.Position != effect.Expected || member.Area != effect.SourceArea)
            throw new InvalidOperationException("Town movement target no longer satisfies its plan.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, MoveToTownEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        if (effect.PersistsPosition)
            await persistence.PersistAsync(context.AccountId, context.CharacterId, effect.Destination, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        transactions.Current!.Enlist(new object(), () => new Commit(onlineCharacters, sessions, context, effect));
        return new TownPositionChange(effect.Expected, effect.Destination);
    }
    private sealed class Commit(IOnlineCharacterRuntimeDomainService onlineCharacters, ISelectedSessionDomainService sessions,
        EffectExecutionContext context, MoveToTownEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (context.Session!.AccountId == context.AccountId && sessions.IsInWorld(context.Session!))
            {
                onlineCharacters.CrossInto(context.Session!,
                    new(effect.ChannelId, effect.Destination.TownId, effect.Destination.AreaId),
                    effect.Destination.X, effect.Destination.Y, false, effect.Destination.Direction);
            }
            return ValueTask.CompletedTask;
        }
    }
}
