using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMailItemDefinitionSource(PvfArchiveProvider provider)
    : IMailItemDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, MailItemDefinition?> cache = [];
    private Dictionary<int, string>? equipmentPaths;
    private Dictionary<int, string>? stackablePaths;

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
        equipmentPaths ??= PvfListFile.Read(archive, "equipment/equipment.lst")
            .ToDictionary(entry => entry.Id, entry => entry.RelativePath);
        stackablePaths ??= PvfListFile.Read(archive, "stackable/stackable.lst")
            .ToDictionary(entry => entry.Id, entry => entry.RelativePath);
        var path = equipmentPaths.TryGetValue(itemId, out var equipment)
            ? "equipment/" + equipment
            : stackablePaths.TryGetValue(itemId, out var stackable)
                ? "stackable/" + stackable
                : null;
        if (path is null || archive.FindFile(path) < 0)
            return null;

        var sections = PvfNpcShopDefinitionSource.Sections(archive, path);
        var attach = sections.Where(section => section.Name == "[attach type]").ToArray();
        var raw = attach.Length == 1 && attach[0].Values.Count == 1
                  && attach[0].Values[0].IsString
            ? archive.ResolveString(attach[0].Values[0].Value)
            : string.Empty;
        var limit = sections.Where(section => section.Name == "[trade limit max]").ToArray();
        int? tradeLimitMax = limit.Length == 1 && limit[0].Values.Count == 1
            && limit[0].Values[0].Kind == PvfTokenKind.Int32
            ? limit[0].Values[0].Value
            : null;
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
