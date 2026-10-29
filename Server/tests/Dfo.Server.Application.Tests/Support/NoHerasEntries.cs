using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>Heras with no daily entries and nothing used, for entry-state fixtures that store no quotas.</summary>
internal sealed class NoHerasEntries : IHerasQuotaDomainService
{
    private static readonly HerasQuotaSnapshot None = new(DateOnly.MinValue, 0, null, null);
    public ValueTask<HerasQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct) => ValueTask.FromResult(None);
    public ValueTask<HerasQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult(None);
    public ValueTask ValidateAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask ApplyAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct) => throw new NotSupportedException();
}
