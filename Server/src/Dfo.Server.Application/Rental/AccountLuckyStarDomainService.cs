using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Rental;

internal sealed class AccountLuckyStarDomainService(ILuckyStarDomainStore store, ILuckyStarRuleService rules,
    ICharacterDomainService characters, ICharacterTable owners, IDungeonRunDomainService runs,
    IDomainTransactionAccessor transactions, IEventService? events = null) : IAccountLuckyStarDomainService
{
    public async ValueTask<int> BalanceAsync(AccountId account,CancellationToken ct) =>
        await store.ReadAsync(account,ct).ConfigureAwait(false) ?? 0;
    private async ValueTask<int?> BeginAsync(AccountId account,CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Lucky stars require a transaction.");
        await store.AcquireWriteAsync(ct).ConfigureAwait(false);
        return await store.ReadAsync(account,ct).ConfigureAwait(false);
    }
    private async ValueTask<LuckyStarMutation> WriteAsync(AccountId account,int previous,int next,CancellationToken ct,bool notifyGain=false)
    {
        try
        {
            if (previous != next)
            {
                if (!await store.CompareExchangeAsync(account,previous,next,ct).ConfigureAwait(false))
                    throw new InvalidOperationException("Lucky star balance changed during settlement.");
                if(events is not null) await events.EnqueueAsync(new LuckyStarChanged(account,next,notifyGain && next>previous),ct).ConfigureAwait(false);
            }
            return new(LuckyStarRejection.None,next,next-previous);
        }
        catch { transactions.Current?.RequireRollback(); throw; }
    }
    public async ValueTask<LuckyStarMutation> GrantAsync(AccountId account,int amount,bool allowPartial,CancellationToken ct)
    {
        var balance=await BeginAsync(account,ct).ConfigureAwait(false);
        if (balance is null) return new(LuckyStarRejection.UnknownOwner,0);
        if (amount<=0) return new(LuckyStarRejection.InvalidCount,balance.Value);
        var space=Math.Max(0,rules.Catalog.Limit-balance.Value);
        if (!allowPartial && amount>space) return new(LuckyStarRejection.LimitReached,balance.Value);
        return await WriteAsync(account,balance.Value,balance.Value+Math.Min(space,amount),ct,true).ConfigureAwait(false);
    }
    public async ValueTask<LuckyStarMutation> SpendAsync(AccountId account,int amount,CancellationToken ct)
    {
        var balance=await BeginAsync(account,ct).ConfigureAwait(false);
        if (balance is null) return new(LuckyStarRejection.UnknownOwner,0);
        if (amount<=0) return new(LuckyStarRejection.InvalidCount,balance.Value);
        if (balance<amount) return new(LuckyStarRejection.InsufficientStars,balance.Value);
        return await WriteAsync(account,balance.Value,balance.Value-amount,ct).ConfigureAwait(false);
    }
    public async ValueTask<LuckyStarMutation> PurchaseAsync(AccountId account,CharacterId character,int count,CancellationToken ct)
    {
        var balance=await BeginAsync(account,ct).ConfigureAwait(false);
        if (balance is null || !await owners.BelongsToAsync(character,account,ct).ConfigureAwait(false)) return new(LuckyStarRejection.UnknownOwner,balance??0);
        var price=(long)count*rules.Catalog.GoldPerStar;
        if (count<=0 || price>int.MaxValue) return new(LuckyStarRejection.InvalidCount,balance.Value);
        if ((long)balance.Value+count>rules.Catalog.Limit) return new(LuckyStarRejection.LimitReached,balance.Value);
        try
        {
            if (await characters.Get(character).TrySpendGoldAsync((int)price,ct).ConfigureAwait(false) is not {} gold)
                return new(LuckyStarRejection.InsufficientGold,balance.Value);
            return (await WriteAsync(account,balance.Value,balance.Value+count,ct).ConfigureAwait(false)) with {Gold=gold};
        }
        catch { transactions.Current?.RequireRollback(); throw; }
    }
    public async ValueTask<LuckyStarMutation> GrantFromItemAsync(AccountId account,int itemId,CancellationToken ct) =>
        await rules.RentPointsAsync(itemId,ct).ConfigureAwait(false) is {} amount
            ? await GrantAsync(account,amount,false,ct).ConfigureAwait(false)
            : new(LuckyStarRejection.InvalidItem,await BalanceAsync(account,ct).ConfigureAwait(false));
    public async ValueTask<LuckyStarMutation> GrantForClearAsync(AccountId account,CharacterId character,DungeonRunId runId,CancellationToken ct)
    {
        var balance=await BeginAsync(account,ct).ConfigureAwait(false);
        var run=runs.Find(character);
        if (balance is null || run is not {ClearOpen:true} || run.RunId!=runId || runId.Value==Guid.Empty
            || !await owners.BelongsToAsync(character,account,ct).ConfigureAwait(false)
            || !(run.Participants.Any(x=>x.CharacterId==character && x.AccountId==account)
                || run.Participant is {} participant && participant.CharacterId==character && participant.AccountId==account))
            return new(LuckyStarRejection.UnknownOwner,balance??0);
        var progress=await characters.Get(character).ProgressAsync(ct).ConfigureAwait(false);
        var amount=progress is null?0:await rules.ClearRewardAsync(run.Layout.DungeonId,progress.Level,ct).ConfigureAwait(false);
        if (!await store.RecordClearAsync(runId,character,ct).ConfigureAwait(false) || amount<=0) return new(LuckyStarRejection.None,balance.Value);
        return await WriteAsync(account,balance.Value,balance.Value+Math.Min(amount,Math.Max(0,rules.Catalog.Limit-balance.Value)),ct,true).ConfigureAwait(false);
    }
}
