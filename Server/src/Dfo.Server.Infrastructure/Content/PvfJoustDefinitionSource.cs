using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfJoustDefinitionSource(PvfArchiveProvider provider, IItemCatalogSource names,
    IPackageOpeningDefinitionSource items) : IJoustDefinitionSource
{
    private const string Path = "event/chn_event/chn_joust.evt";
    public async ValueTask<JoustDefinition> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, Path);
        var root = document.AtRoot();
        int Single(string name) => root.Read(PvfField.Int32(name));
        var knights = new List<JoustKnight>();
        List<PvfSection>? fields = null;
        foreach (var section in document.Sections)
        {
            if (section.Name == "[knight]") fields = [];
            else if (section.Name == "[/knight]" && fields is not null)
            {
                int[] Numbers(string name) => fields.Where(s => s.Name == name)
                    .SelectMany(s => PvfField.Int32List(name).Decode(archive, s.Values, Path)).ToArray();
                var name = fields.Single(s => s.Name == "[knight name]");
                knights.Add(new(Numbers("[index]").Single(),
                    PvfField.String(name.Name).Decode(archive, name.Values, Path),
                    Numbers("[attack type]").Single(), Numbers("[win]"), Numbers("[loss]")));
                fields = null;
            }
            else fields?.Add(section);
        }
        async ValueTask<JoustItem> Item(int id)
        {
            var declaration = await items.GetRewardAsync(id, ct) ?? throw new InvalidDataException($"马战物品 {id} 未声明。");
            if (!declaration.Supported || declaration.Placement.WorldClass != ItemWorldClass.Interchangeable)
                throw new InvalidDataException($"马战物品 {id} 不是支持的堆叠物。");
            // Only this event's grants discard the archive's obsolete campaign expiry.
            return new(id, (await names.FindAsync(id, ct))?.Name,
                declaration.AbsoluteExpiration > 0 ? DateTimeOffset.FromUnixTimeSeconds(declaration.AbsoluteExpiration) : null)
            { GrantTemplate = new() { ItemId = id, IsPile = true, Type = declaration.Placement.Traits,
                LegacyKind = declaration.Placement.ItemKind, Durability = declaration.Durability,
                SealFlag = declaration.Sealed ? (byte)1 : (byte)0, ExpireTime = 0 } };
        }
        var materials = new List<JoustItem>();
        foreach (var id in root.Read(PvfField.Int32List("[material]"))) materials.Add(await Item(id));
        return new(Path, Single("[min level]"), Single("[max betting]"), [..materials],
            await Item(Single("[reward]")), await Item(Single("[betting reward]")), knights.OrderBy(k => k.Index).ToArray());
    }
}
