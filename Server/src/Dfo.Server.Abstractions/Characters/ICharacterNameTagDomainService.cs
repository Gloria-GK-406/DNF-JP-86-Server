using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public sealed record NameTagPurchase(int ItemId, int DurationDays);

public interface ICharacterNameTagDomainStore
{
    ValueTask<CharacterNameTag?> ReadAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<bool> TryWriteAsync(AccountId account, CharacterId character, CharacterNameTag expected,
        CharacterNameTag next, CancellationToken ct);
}

public interface ICharacterNameTagRuleService
{
    CharacterNameTag Effective(CharacterNameTag state, DateTimeOffset at);
    CharacterNameTag Purchase(CharacterNameTag current, IReadOnlyList<NameTagPurchase> purchases, DateTimeOffset at);
}

public interface ICharacterNameTagDomainService
{
    ValueTask<CharacterNameTag> ReadEffectiveAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask PurchaseAsync(AccountId account, CharacterId character, IReadOnlyList<NameTagPurchase> purchases,
        DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> ExpireAsync(SelectedSessionIdentity session, CancellationToken ct);
}

public sealed record CharacterNameTagExpired(AccountId AccountId, CharacterId CharacterId, string ConnectionId, long ConnectionGeneration)
    : Dfo.Server.Abstractions.DomainEvents.IDomainEvent;
