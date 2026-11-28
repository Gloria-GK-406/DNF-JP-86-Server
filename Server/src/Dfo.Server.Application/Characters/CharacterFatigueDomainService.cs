using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterFatigueDomainService(ICharacterTable characters,
    IPremiumServiceDomainService premiums, IServerConfigService settings, ICharacterFatigueTable table,
    IDailyResetRuleService days, TimeProvider clock, IDomainTransactionAccessor transactions,
    IFatigueConsumptionQuery consumption) : ICharacterFatigueDomainService
{
    public async ValueTask<bool> CanEnterDungeonAsync(CharacterId id, CancellationToken ct) =>
        !await consumption.ShouldConsumeAsync(ct).ConfigureAwait(false) || (await BalanceOfAsync(id, ct).ConfigureAwait(false)).Remaining > 0;
    private readonly ushort baseTotal = settings.GetConfig<ushort>("Fatigue:BaseTotal");
    private async ValueTask<ushort> TotalAsync(CharacterId id, CancellationToken token)
    {
        var account = await characters.FindOwnerAsync(id, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character {id.Value} does not exist.");
        return checked((ushort)(baseTotal + await premiums.For(account).GetExtraFatigueAsync(token).ConfigureAwait(false)));
    }
    private CharacterFatigueRow Current(CharacterFatigueRow? row, DateTimeOffset now) =>
        row is not null && days.IsCurrentPeriod(row.UpdatedAt, now) ? row : new(0, 0, now);
    public ValueTask<CharacterFatigueBalance> BalanceOfAsync(CharacterId id, CancellationToken token) =>
        BalanceAtAsync(id, clock.GetUtcNow(), token);
    public async ValueTask<CharacterFatigueBalance> BalanceAtAsync(CharacterId id, DateTimeOffset at, CancellationToken token)
    {
        var total = await TotalAsync(id, token).ConfigureAwait(false);
        var row = Current(await table.ReadAsync(id, token).ConfigureAwait(false), at);
        return new(total, checked((ushort)Math.Min(total, row.Used))) { ConsumedToday = row.ConsumedToday };
    }
    public async ValueTask<int> ConsumedOnAsync(CharacterId id, DateOnly day, CancellationToken token) =>
        await table.ReadAsync(id, token).ConfigureAwait(false) is { } row && days.PeriodDay(row.UpdatedAt) == day
            ? row.ConsumedToday : 0;
    public ValueTask SpendAsync(CharacterId id, ushort points, CancellationToken token) => SpendBatchAsync([id], points, token);
    public ValueTask SpendBatchAsync(IReadOnlyList<CharacterId> characters, ushort points, CancellationToken token) =>
        SpendBatchAsync(characters.Select(character => new CharacterFatigueCharge(character, points)).ToArray(), token);

    public async ValueTask SpendBatchAsync(IReadOnlyList<CharacterFatigueCharge> charges, CancellationToken token)
    {
        if (transactions.Current is null || transactions.Current.IsReadOnly)
            throw new InvalidOperationException("Fatigue changes require a writable transaction.");
        if (!await consumption.ShouldConsumeAsync(token).ConfigureAwait(false))
        {
            if (charges.Any(charge => charge.RequireFullCharge))
                throw new InvalidOperationException("Fatigue consumption changed after preparing a required charge.");
            return;
        }
        foreach (var charge in charges)
            await ChangeAsync(charge.Character, charge.Points, false, token, charge.RequireFullCharge).ConfigureAwait(false);
    }
    public ValueTask RefundAsync(CharacterId id, ushort points, CancellationToken token) => ChangeAsync(id, points, true, token);
    private async ValueTask ChangeAsync(CharacterId id, ushort points, bool restore, CancellationToken token,
        bool requireFullCharge = false)
    {
        if (transactions.Current is null || transactions.Current.IsReadOnly) throw new InvalidOperationException("Fatigue changes require a writable transaction.");
        var total = await TotalAsync(id, token).ConfigureAwait(false);
        var old = await table.ReadAsync(id, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var current = Current(old, now);
        var used = Math.Min(total, current.Used);
        var actual = restore ? Math.Min(used, points) : Math.Min(total - used, points);
        if (requireFullCharge && actual != points)
            throw new InvalidOperationException("Fatigue balance no longer covers the prepared charge.");
        var updated = new CharacterFatigueRow(restore ? used - actual : used + actual,
            restore ? current.ConsumedToday : checked(current.ConsumedToday + actual), now);
        if (await table.TryWriteAsync(id, old, updated, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Fatigue changed concurrently.");
    }
}
