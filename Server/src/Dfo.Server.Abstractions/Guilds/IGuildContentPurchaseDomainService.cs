using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildContentPurchaseDomainService
{
    ValueTask<GuildMutationResult> PurchaseAsync(CharacterId actor, uint productId, CancellationToken ct);
}
