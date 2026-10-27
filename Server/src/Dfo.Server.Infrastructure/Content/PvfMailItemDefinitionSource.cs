using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMailItemDefinitionSource(PvfArchiveProvider provider)
    : IMailItemDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<string> AttachTypeField = PvfField.String("[attach type]", optional: true);
        internal static readonly PvfField<int> TradeLimitMaxField = PvfField.Int32("[trade limit max]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, MailItemDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? equipmentPaths;
    private IReadOnlyDictionary<int, string>? stackablePaths;

    public string Name => "mail-item-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            EquipmentPathsIndex(archive);
            StackablePathsIndex(archive);
            return ValueTask.FromResult(equipmentPaths.Count + stackablePaths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipmentPaths))]
    private void EquipmentPathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipmentPaths ??= PvfListFile.Paths(archive, "equipment/equipment.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackablePaths))]
    private void StackablePathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackablePaths ??= PvfListFile.Paths(archive, "stackable/stackable.lst");
    }

    public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(itemId, out var definition))
                cache[itemId] = definition = this.Read(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    private MailItemDefinition? Read(int itemId)
    {
        var archive = provider.Archive;
        EquipmentPathsIndex(archive);
        StackablePathsIndex(archive);
        var path = equipmentPaths.TryGetValue(itemId, out var equipment)
            ? "equipment/" + equipment
            : stackablePaths.TryGetValue(itemId, out var stackable)
                ? "stackable/" + stackable
                : null;
        if (path is null || archive.FindFile(path) < 0)
            return null;

        var document = PvfDocument.Open(archive, path);
        var raw = document.TryRead(Fields.AttachTypeField, out var attachType) ? attachType : string.Empty;
        int? tradeLimitMax = document.TryRead(Fields.TradeLimitMaxField, out var limit) ? limit : null;
        return new MailItemDefinition(AttachTypeOf(raw), raw, tradeLimitMax);
    }

    private static MailItemAttachType AttachTypeOf(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "[free]" => MailItemAttachType.Free,
        "[trade]" => MailItemAttachType.Trade,
        "[trade delete]" => MailItemAttachType.TradeDelete,
        "[sealing]" => MailItemAttachType.Sealing,
        "[sealing trade]" => MailItemAttachType.SealingTrade,
        "[account]" => MailItemAttachType.Account,
        "[trade limit]" => MailItemAttachType.TradeLimit,
        _ => MailItemAttachType.Unknown,
    };
}
