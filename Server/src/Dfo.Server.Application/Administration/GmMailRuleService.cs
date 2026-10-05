using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Administration;

internal sealed partial class GmMailRuleService(IPackageOpeningRuleService openings, IItemPlacementRuleService placements,
    IGmMailEquipmentRuleService equipmentRules, IGmMailItemCatalogSource catalog, TimeProvider clock) : IGmMailRuleService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public GmMailInput Normalize(GmMailInput input, bool sending)
    {
        if (input is null || input.Title is null || input.Body is null || input.RecipientIds is null || input.Items is null
            || input.RecipientIds.Length > 100 || input.RecipientIds.Any(x => x <= 0) || input.Items.Length > 10
            || input.Items.Any(x => x is null || x.ItemId <= 0 || x.Count <= 0) || input.Gold < 0)
            throw Invalid("邮件字段、收件人或附件数量无效。");
        var title = Text(input.Title); var body = Text(input.Body);
        if (sending && string.IsNullOrWhiteSpace(body)) body = "此为官方邮件，无需回复。";
        var items = input.Items.Select(item => item with { Equipment = NormalizeOptions(item.Equipment) }).ToArray();
        if (items.Select(item => (item.ItemId, item.Equipment)).Distinct().Count() != items.Length)
            throw Invalid("相同物品和配置请合并数量；不同装备配置可以分别添加。");
        var result = new GmMailInput(title, body, input.RecipientIds.Distinct().Order().ToArray(), items, input.Gold);
        if (sending && (result.RecipientIds.Length == 0 || Utf8.GetByteCount(DisplayText(result)) > 511))
            throw Invalid("请选择收件人，实际显示内容最多 511 个 UTF-8 字节（含留空正文的默认文案）。");
        return result;
    }
    public string DisplayText(GmMailInput input) => input.Title.Length == 0 || input.Title == input.Body ? input.Body : input.Title + "\n" + input.Body;
    public string PayloadHash(GmMailInput input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    public async ValueTask<IReadOnlyList<GmMailPreparedItem>> PrepareItemsAsync(GmMailInput input, DateTimeOffset now, CancellationToken token)
    {
        if (input.Items.Length == 0) return [];
        IReadOnlyList<OpeningPreparedGrant> prepared;
        try { prepared = await openings.PrepareAsync(input.Items.Select(x => new OpeningReward(x.ItemId, x.Count)).ToArray(), now, token); }
        catch (OpeningRejectedException) { throw Invalid("附件模板不存在、不支持或已过期。"); }
        var output = new List<GmMailPreparedItem>(); var expanded = 0;
        if (prepared.Count != input.Items.Length) throw Invalid("附件准备结果与请求不一致。");
        for (var index = 0; index < prepared.Count; index++)
        {
            var grant = prepared[index]; var declaration = grant.Item; var requested = input.Items[index];
            var placement = await placements.PlacementForAsync(requested.ItemId, token);
            if (grant.Reward.ItemId != requested.ItemId || grant.Reward.Count != requested.Count
                || declaration.ItemId != requested.ItemId || !declaration.Supported || declaration.DefaultPeriodDays != 0
                || declaration.AbsoluteExpiration != 0 || declaration.PremiumType != 0 || grant.Expiration != 0
                || placement is null || placement.ItemKind is 0 or 5 or 6 or 7 or 8
                || declaration.Placement.ItemKind != placement.ItemKind || declaration.Placement.WorldClass != placement.WorldClass
                || declaration.Placement.Destination.Space == BusinessInventorySpace.VirtualResource
                || placement.Destination.Space == BusinessInventorySpace.VirtualResource
                || placement.WorldClass is not (ItemWorldClass.Equipment or ItemWorldClass.Interchangeable))
                throw Invalid($"物品 {requested.ItemId} 不属于支持的永久普通装备或堆叠物。");
            var equipment = placement.WorldClass == ItemWorldClass.Equipment;
            expanded = checked(expanded + (equipment ? Math.Min(requested.Count, 11) : 1));
            if (equipment && requested.Count > 10 || expanded > 10) throw Invalid("每人最多 10 个展开物品附件。");
            var core = new ItemSnapshot
            {
                ItemId = requested.ItemId, Count = equipment ? 1 : requested.Count, IsPile = !equipment,
                Type = placement.Traits, LegacyKind = placement.ItemKind, Marker16 = -1,
                RandomOptionChangedIndex = byte.MaxValue,
                Durability = declaration.Durability, SealFlag = declaration.Sealed ? (byte)1 : (byte)0
            };
            if (requested.Equipment is { } options)
            {
                if (!equipment) throw Invalid($"物品 {requested.ItemId} 是堆叠物，不能配置装备属性。");
                core = await equipmentRules.ConfigureAsync(core, options, token);
            }
            var name = (await catalog.FindAsync(requested.ItemId, token))?.Name;
            output.Add(new(requested, new(requested.ItemId, requested.Count, equipment ? "equipment" : "stackable",
                core.Durability, core.SealFlag != 0, name, core.ReinforcementLevel, core.AmplifyType, core.AmplifyValue), core));
        }
        return output;
    }
    private static string Text(string value)
    {
        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) throw Invalid("邮件文字包含不支持的控制字符。");
        try { if (Utf8.GetByteCount(text) > 511) throw Invalid("单个文本字段最多 511 个 UTF-8 字节。"); }
        catch (EncoderFallbackException) { throw Invalid("邮件文字包含无效字符。"); }
        return text;
    }
    private static GmMailEquipmentOptions? NormalizeOptions(GmMailEquipmentOptions? options)
    {
        if (options is null) return null;
        if (options.Seal is not ("default" or "unsealed") || options.Growth is not ("none" or "reinforcement" or "amplification")
            || options.Level is < 0 or > 31 || options.Growth == "none" && options.Level != 0
            || options.Growth == "reinforcement" && options.Level == 0
            || (options.Growth == "amplification" ? options.AmplifyType is < 1 or > 4 : options.AmplifyType != 0))
            throw Invalid("装备封装、成长模式、等级或异次元属性组合无效。");
        // Omitting the default configuration preserves the exact JSON/hash of pre-extension mail.
        return options == new GmMailEquipmentOptions() ? null : options;
    }
    private static GmMailException Invalid(string text) => new(GmMailRefusal.Invalid, text);
}
