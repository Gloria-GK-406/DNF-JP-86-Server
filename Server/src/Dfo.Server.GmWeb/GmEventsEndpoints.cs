using Dfo.Server.HttpApi;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Events;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
namespace Dfo.Server.GmWeb;
internal static class GmEventsEndpoints
{
    private const string Prefix="/api/gm/v1/events";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    internal static void Map(RouteGroupBuilder group)
    {
        var permission = new GmRequiredPermission(GmPermission.All);
        Map("GET", "", HttpRouteOperation.Events, (HttpContext c) => new QueryGmEventsCommand(Secret(c)));
        Map("GET", "/weekly-attendance/settings", HttpRouteOperation.WeeklyAttendance, (HttpContext c) => new QueryGmWeeklyAttendanceSettingsCommand(Secret(c)));
        Map("GET", "/joust/settings", HttpRouteOperation.JoustSettings, (HttpContext c) => new QueryGmJoustSettingsCommand(Secret(c)));
        Map("GET", "/joust/current-round", HttpRouteOperation.JoustRound, (HttpContext c) => new QueryGmJoustCurrentRoundCommand(Secret(c)));
        Map("POST", "/joust/current-round/champion", HttpRouteOperation.JoustChampion, async (HttpContext c) => new ChangeGmJoustChampionCommand(Secret(c), await ReadJoust<JoustChampionChange>(c, "场次")));
        Map("POST", "/joust/settings", HttpRouteOperation.JoustChange, async (HttpContext c) => new ChangeGmJoustSettingsCommand(Secret(c), await ReadJoust<JoustSettingsChange>(c, "活动")));
        Map("POST", "", HttpRouteOperation.EventsChange, async (HttpContext c) => new ChangeGmEventsCommand(Secret(c), await Read(c)));
        void Map(string method, string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, method, Prefix + path, operation, parser, permission);
    }
    private static async Task<T> ReadJoust<T>(HttpContext c, string kind)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is > 32768) throw new ArgumentException(kind + "请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await c.Request.ReadFromJsonAsync<T>(Json, c.RequestAborted) ?? throw new ArgumentException(kind + "字段缺失。");
    }
    private static string Secret(HttpContext c)=>c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<EventChange> Read(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is >32768) throw new ArgumentException("活动请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly:false } limit) limit.MaxRequestBodySize=32768;
        return await c.Request.ReadFromJsonAsync<EventChange>(Json,c.RequestAborted) ?? throw new ArgumentException("活动字段缺失。");
    }
}
