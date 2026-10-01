using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterFatigueDomainService(ICharacterTable characters,
    IPremiumServiceDomainService premiums, IServerConfigService settings, ICharacterFatigueTable table,
    IDailyResetRuleService days, TimeProvider clock, IDomainTransactionAccessor transactions) : ICharacterFatigueDomainService
{
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
    public ValueTask SpendAsync(CharacterId id, ushort points, CancellationToken token) => ChangeAsync(id, points, false, token);
    public ValueTask RefundAsync(CharacterId id, ushort points, CancellationToken token) => ChangeAsync(id, points, true, token);
    private async ValueTask ChangeAsync(CharacterId id, ushort points, bool restore, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Fatigue changes require a transaction.");
        var total = await TotalAsync(id, token).ConfigureAwait(false);
        var old = await table.ReadAsync(id, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var current = Current(old, now);
        var used = Math.Min(total, current.Used);
        var actual = restore ? Math.Min(used, points) : Math.Min(total - used, points);
        var updated = new CharacterFatigueRow(restore ? used - actual : used + actual,
            restore ? current.ConsumedToday : checked(current.ConsumedToday + actual), now);
        if (await table.TryWriteAsync(id, old, updated, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Fatigue changed concurrently.");
    }
}
