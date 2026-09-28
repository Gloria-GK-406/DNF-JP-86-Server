using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

// Total is real; spending and refunds remain unimplemented until daily accounting is introduced.
internal sealed class UnaccountedCharacterFatigueDomainService(
    ICharacterTable characters, IPremiumServiceDomainService premiums, IServerConfigService settings)
    : ICharacterFatigueDomainService
{
    private readonly ushort baseTotal = settings.GetConfig<ushort>("Fatigue:BaseTotal");

    public async ValueTask<CharacterFatigueBalance> BalanceOfAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var account = await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Character {characterId.Value} does not exist.");
        var extra = await premiums.For(account).GetExtraFatigueAsync(cancellationToken).ConfigureAwait(false);
        return new(checked((ushort)(baseTotal + extra)), 0);
    }

    public ValueTask SpendAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask RefundAsync(CharacterId characterId, ushort points, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
