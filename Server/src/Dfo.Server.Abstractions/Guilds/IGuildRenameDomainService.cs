using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildRenameAuthorization(GuildId Guild, string OriginalName, string Name);

public sealed record GuildRenameCheck(GuildMutationStatus Status, GuildRenameAuthorization? Authorization = null);

public sealed record GuildRenameReceipt(ItemSnapshot? RemainingCard);

public interface IGuildRenameDomainService
{
    ValueTask<GuildRenameCheck> CheckAsync(CharacterId actor, string name, CancellationToken ct);

    ValueTask<GuildRenameReceipt?> RenameAsync(SelectedSessionIdentity session, GuildRenameAuthorization authorization,
        BusinessSlot source, IEffectFootprintRecorder footprint, CancellationToken ct);
    bool IsGoldProduct(uint productId);
    ValueTask<GuildMutationResult> RenameWithGoldAsync(CharacterId actor, GuildRenameAuthorization authorization,
        uint productId, CancellationToken ct);
}
