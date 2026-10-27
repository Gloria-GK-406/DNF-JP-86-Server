using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;

namespace Dfo.Server.Application.Effects;

public abstract class EffectHandler<TEffect> : IEffectHandler where TEffect : DomainEffect
{
    public Type EffectType => typeof(TEffect);
    public abstract ValueTask ValidateAsync(EffectExecutionContext context, TEffect effect, CancellationToken ct);
    public abstract ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, TEffect effect, CancellationToken ct);
    ValueTask IEffectHandler.ValidateAsync(EffectExecutionContext context, DomainEffect effect, CancellationToken ct) =>
        ValidateAsync(context, (TEffect)effect, ct);
    ValueTask<EffectChange> IEffectHandler.ApplyAsync(EffectExecutionContext context, DomainEffect effect, CancellationToken ct) =>
        ApplyAsync(context, (TEffect)effect, ct);
}

internal sealed class ApplyEffectsDomainService(IEnumerable<IEffectHandler> handlers,
    IDomainTransactionAccessor transactions, ICharacterDomainStore characters,
    ISelectedSessionDomainService sessions, IInventoryEffectPreflightDomainService inventory) : IApplyEffectsDomainService
{
    private readonly IReadOnlyDictionary<Type, IEffectHandler> registered = handlers.ToDictionary(x => x.EffectType);
    public async ValueTask<ApplyEffectsResult> ApplyAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effects);
        context.Footprint.DemandEffects(context.Owner);
        var transaction = transactions.Current;
        if (transaction is null)
            throw new InvalidOperationException("Effects require an active use case transaction.");
        if (!await characters.SessionIn(transaction).HoldsAsync(new(context.AccountId, context.CharacterId), ct).ConfigureAwait(false))
            throw new InvalidOperationException("Effect character does not belong to this account.");
        context.Footprint.DemandEffects(context.Owner);
        if (context.Session is { } session &&
            (session.AccountId != context.AccountId || session.CharacterId != context.CharacterId
                || !sessions.IsInWorld(session)))
            throw new InvalidOperationException("Effect session disagrees with its owner.");
        var work = effects.Select(effect => (Effect: effect,
            Handler: registered.TryGetValue(effect.GetType(), out var handler) ? handler
                : throw new NotSupportedException($"No effect handler registered for {effect.GetType().Name}.")))
            .ToArray();
        var preview = await inventory.PreviewAsync(context, effects, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        if (preview.Rejection != InventoryEffectRejection.None)
            throw new InventoryEffectException(preview.Rejection);
        foreach (var (effect, handler) in work)
        {
            context.Footprint.DemandEffects(context.Owner);
            await handler.ValidateAsync(context, effect, ct).ConfigureAwait(false);
            context.Footprint.DemandEffects(context.Owner);
        }
        var applied = new List<AppliedEffect>(work.Length);
        foreach (var (effect, handler) in work)
        {
            context.Footprint.DemandEffects(context.Owner);
            applied.Add(new(effect, await handler.ApplyAsync(context, effect, ct).ConfigureAwait(false)));
        }
        context.Footprint.RecordEffects(context.Owner, applied);
        return new(applied.AsReadOnly());
    }
}
