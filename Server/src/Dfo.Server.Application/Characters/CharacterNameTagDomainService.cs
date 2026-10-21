using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterNameTagRuleService : ICharacterNameTagRuleService
{
    public CharacterNameTag Effective(CharacterNameTag state, DateTimeOffset at) =>
        state.ItemId > 0 && state.ExpiresAtUnixSeconds > at.ToUnixTimeSeconds() ? state : CharacterNameTag.Empty;

    public CharacterNameTag Purchase(CharacterNameTag current, IReadOnlyList<NameTagPurchase> purchases, DateTimeOffset at)
    {
        var state = Effective(current, at);
        foreach (var purchase in purchases)
        {
            if (purchase.ItemId <= 0 || purchase.DurationDays <= 0)
                throw new InvalidDataException("Invalid name tag purchase declaration.");
            var start = state.ItemId == purchase.ItemId ? state.ExpiresAtUnixSeconds : at.ToUnixTimeSeconds();
            var end = checked(start + purchase.DurationDays * 86400L);
            if (end <= at.ToUnixTimeSeconds() || end > uint.MaxValue)
                throw new OverflowException("Name tag expiration cannot fit its wire timestamp.");
            state = new(purchase.ItemId, end);
        }
        return state;
    }
}

internal sealed class CharacterNameTagDomainService(ICharacterNameTagDomainStore store,
    ICharacterNameTagRuleService rules, TimeProvider clock, ISelectedSessionDomainService sessions,
    IAuthenticatedGamePresenceQuery online, IEventService events)
    : ICharacterNameTagDomainService
{
    public async ValueTask<CharacterNameTag> ReadEffectiveAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        rules.Effective(await store.ReadAsync(account, character, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Name tag owner is unavailable."), clock.GetUtcNow());

    public async ValueTask PurchaseAsync(AccountId account, CharacterId character, IReadOnlyList<NameTagPurchase> purchases,
        DateTimeOffset at, CancellationToken ct)
    {
        var before = await store.ReadAsync(account, character, ct).ConfigureAwait(false)
            ?? throw new CeraShopPurchaseRejectedException(CeraShopRejection.NoSession, "Name tag owner is unavailable.");
        CharacterNameTag after;
        try { after = rules.Purchase(before, purchases, at); }
        catch (OverflowException error)
        { throw new CeraShopPurchaseRejectedException(CeraShopRejection.UnsupportedProduct, error.Message); }
        if (!await store.TryWriteAsync(account, character, before, after, ct).ConfigureAwait(false))
            throw new CeraShopPurchaseRejectedException(CeraShopRejection.StateChanged, "Name tag changed during settlement.");
    }

    public async ValueTask<bool> ExpireAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session || !sessions.IsInWorld(session)
            || !online.IsActiveExact(session)) return false;
        var before = await store.ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        if (before is null || before == CharacterNameTag.Empty || rules.Effective(before, clock.GetUtcNow()) != CharacterNameTag.Empty)
            return false;
        if (!await store.TryWriteAsync(session.AccountId, session.CharacterId, before, CharacterNameTag.Empty, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Name tag changed during expiration.");
        await events.EnqueueAsync(new CharacterNameTagExpired(session.AccountId, session.CharacterId,
            session.ConnectionId.Value, session.ConnectionGeneration), ct).ConfigureAwait(false);
        return true;
    }
}
