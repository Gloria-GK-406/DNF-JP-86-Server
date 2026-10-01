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
    ISelectedSessionDomainService sessions, IInventoryEffectPreflightDomainService inventory, EffectOperationContext? operations = null) : IApplyEffectsDomainService
{
    private readonly IReadOnlyDictionary<Type, IEffectHandler> registered = handlers.ToDictionary(x => x.EffectType);
    public async ValueTask<ApplyEffectsResult> ApplyAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effects);
        var transaction = transactions.Current;
        if (transaction is null)
            throw new InvalidOperationException("Effects require an active use case transaction.");
        if (!await characters.SessionIn(transaction).HoldsAsync(new(context.AccountId, context.CharacterId), ct).ConfigureAwait(false))
            throw new InvalidOperationException("Effect character does not belong to this account.");
        if (context.Session is { } session &&
            (session.AccountId != context.AccountId || session.CharacterId != context.CharacterId
                || sessions.FindByCharacter(context.CharacterId) != session))
            throw new InvalidOperationException("Effect session disagrees with its owner.");
        var work = effects.Select(effect => (Effect: effect,
            Handler: registered.TryGetValue(effect.GetType(), out var handler) ? handler
                : throw new NotSupportedException($"No effect handler registered for {effect.GetType().Name}.")))
            .ToArray();
        var preview = await inventory.PreviewAsync(context, effects, ct).ConfigureAwait(false);
        if (preview.Rejection != InventoryEffectRejection.None)
            throw new InventoryEffectException(preview.Rejection);
        foreach (var (effect, handler) in work)
            await handler.ValidateAsync(context, effect, ct).ConfigureAwait(false);
        var applied = new List<AppliedEffect>(work.Length);
        foreach (var (effect, handler) in work)
            applied.Add(new(effect, await handler.ApplyAsync(context, effect, ct).ConfigureAwait(false)));
        operations?.Record(context, applied);
        return new(applied.AsReadOnly());
    }
}
