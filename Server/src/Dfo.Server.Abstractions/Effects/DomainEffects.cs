using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Effects;

public abstract record DomainEffect;
public abstract record EffectChange;
public sealed record EffectExecutionContext(AccountId AccountId, CharacterId CharacterId,
    DateTimeOffset At, IEffectFootprintRecorder Footprint, SelectedSessionIdentity? Session = null)
{
    public IEffectFootprintRecorder Footprint { get; } = Footprint ?? throw new ArgumentNullException(nameof(Footprint));
    public EffectFootprintOwner Owner => new(AccountId, CharacterId, Session);
}
public sealed record AppliedEffect(DomainEffect Effect, EffectChange Change);
public sealed record ApplyEffectsResult(IReadOnlyList<AppliedEffect> Applied)
{
    public IEnumerable<TChange> Changes<TChange>() where TChange : EffectChange =>
        Applied.Select(value => value.Change).OfType<TChange>();
}

public interface IApplyEffectsDomainService
{
    ValueTask<ApplyEffectsResult> ApplyAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken cancellationToken);
}

public interface IEffectHandler
{
    Type EffectType { get; }
    ValueTask ValidateAsync(EffectExecutionContext context, DomainEffect effect, CancellationToken cancellationToken);
    ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, DomainEffect effect, CancellationToken cancellationToken);
}
