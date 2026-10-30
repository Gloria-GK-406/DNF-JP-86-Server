using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;
using Dfo.Pvf.Tokens;
using Dfo.Server.Data.Content;
using Dfo.Pvf.Schema;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAvatarAppearanceDefinitionSource(PvfArchiveProvider provider,
    ICeraShopDefinitionSource items) : IAvatarAppearanceDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, bool?> cache = [];

    public async ValueTask<bool?> IsCloneAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) if (cache.TryGetValue(itemId, out var cached)) return cached;
        var item = await items.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        bool? result = null;
        if (item is { Kind: CeraShopItemKind.Avatar })
        {
            var sections = (PvfDocument.TryOpen(provider.Archive, item.Source, PvfPreamble.Ignore)?.Sections ?? []);
            // Aura virtual motion uses another mechanism; do not treat it as a clone costume.
            result = !sections.Any(s => s.Name == "[aurora virtual motion]") && sections.Any(s =>
                s.Name == "[item category]" && s.Values.Any(t => t.IsString
                    && provider.Archive.ResolveString(t.Value).Trim('[', ']') == "clear avatar")
                || s.Name == "[clear avatar]" && s.Values.Any(t => t.Kind == PvfTokenKind.Int32 && t.Value == 1));
        }
        lock (gate) { cache.TryAdd(itemId, result); return cache[itemId]; }
    }
}
