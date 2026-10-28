using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using System.Text;
namespace Dfo.Server.Application.Events;

internal sealed class WeeklyAttendanceRuleService(IWeeklyAttendanceDefinitionSource source, IItemPlacementRuleService items,
    IDungeonDefinitionSource dungeons, IDailyResetRuleService reset, IItemCatalogSource itemNames) : IWeeklyAttendanceRuleService
{
    private static readonly Encoding CalendarEncoding=new UTF8Encoding(false,true);
    public void ValidateCalendarText(string text)
    {
        try { _=CalendarEncoding.GetByteCount(text); }
        catch (EncoderFallbackException e) { throw new ArgumentException("活动展示文案包含无效的 Unicode 字符。",nameof(text),e); }
    }
    public async ValueTask<(WeeklyAttendanceDefinition? Definition,string? Error)> DefinitionAsync(CancellationToken ct)
    {
        try
        {
            var d = await source.ReadAsync(ct);
            if (d.RequiredClears <= 0 || d.TotalWeeks is <= 0 or > 20 || d.WeeklyRewards.Length != d.TotalWeeks
                || !d.WeeklyRewards.Select(r => r.AttendanceNo).Order().SequenceEqual(Enumerable.Range(1,d.TotalWeeks))
                || d.CumulativeRewards.Select(r=>r.AttendanceNo).Distinct().Count()!=d.CumulativeRewards.Length)
                throw new InvalidDataException("签到脚本条件或奖励序列不完整。");
            foreach (var reward in d.WeeklyRewards.Concat(d.CumulativeRewards))
            {
                var placement=await items.PlacementForAsync(reward.ItemId,ct);
                if (reward.AttendanceNo <= 0 || reward.AttendanceNo > d.TotalWeeks || reward.ItemId <= 0 || reward.Count <= 0
                    || placement is null || placement.WorldClass != ItemWorldClass.Interchangeable)
                    throw new InvalidDataException($"签到奖励 {reward.ItemId} 不是可投递的堆叠物品。");
            }
            return (d,null);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return (null,e.Message); }
    }
    public async ValueTask<bool> RecommendedAsync(int dungeonId,int level,CancellationToken ct)
    {
        var d = await dungeons.GetAsync(dungeonId,ct);
        if (d is null || level<=0) return false;
        var min=(int)d.MinimumRequiredLevel;var max=(int)d.BasisLevel;
        if (min<=0 && max<=0) return false;
        if (min<=0)min=max;if(max<=0)max=min;
        if (min>max)(min,max)=(max,min);
        return (long)level+5>=min && Math.Max(1,(long)level-5)<=max;
    }
    public async ValueTask<string?> ItemNameAsync(int itemId,CancellationToken ct)
    {
        var name=(await itemNames.FindAsync(itemId,ct))?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
    public DateTimeOffset WeekStart(DateTimeOffset now)
    {
        var day = reset.PeriodStart(now).ToOffset(IDailyResetRuleService.BusinessOffset);
        return day.AddDays(-(((int)day.DayOfWeek+6)%7)).ToUniversalTime();
    }
}
