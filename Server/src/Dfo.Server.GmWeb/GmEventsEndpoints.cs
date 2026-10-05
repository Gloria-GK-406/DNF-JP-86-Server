using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
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
        var permission=new GmRequiredPermission(GmPermission.All);
        group.MapGet(Prefix,(HttpContext c,ICommandBus bus)=>GmInitializationEndpoints.Safe(c,async()=>
            Results.Ok(await bus.SendAsync(new QueryGmEventsCommand(Secret(c)),c.RequestAborted)))).WithMetadata(permission);
        group.MapGet(Prefix+"/weekly-attendance/settings",(HttpContext c,ICommandBus bus)=>GmInitializationEndpoints.Safe(c,async()=>
            Results.Ok(await bus.SendAsync(new QueryGmWeeklyAttendanceSettingsCommand(Secret(c)),c.RequestAborted)))).WithMetadata(permission);
        group.MapGet(Prefix+"/joust/settings",(HttpContext c,ICommandBus bus)=>GmInitializationEndpoints.Safe(c,async()=>
            Results.Ok(await bus.SendAsync(new QueryGmJoustSettingsCommand(Secret(c)),c.RequestAborted)))).WithMetadata(permission);
        group.MapPost(Prefix+"/joust/settings",(HttpContext c,ICommandBus bus)=>GmInitializationEndpoints.Safe(c,async()=>
        {
            if(!c.Request.HasJsonContentType() || c.Request.ContentLength is >32768)throw new ArgumentException("活动请求必须为 JSON，且不超过 32 KiB。");
            if(c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly:false } limit)limit.MaxRequestBodySize=32768;
            var change=await c.Request.ReadFromJsonAsync<JoustSettingsChange>(Json,c.RequestAborted) ?? throw new ArgumentException("活动字段缺失。");
            return Results.Ok(await bus.SendAsync(new ChangeGmJoustSettingsCommand(Secret(c),change),c.RequestAborted));
        })).WithMetadata(permission);
        group.MapPost(Prefix,(HttpContext c,ICommandBus bus,INotificationBus notices,ISelectedSessionSnapshotQuery online)=>GmInitializationEndpoints.Safe(c,async()=>
        {
            var result=await bus.SendAsync(new ChangeGmEventsCommand(Secret(c),await Read(c)),c.RequestAborted);
            // The configuration has committed; a closed HTTP request cannot undo its online refresh.
            await notices.PublishAsync(new NotificationAudience.TheseSessions(online.Snapshot()),new EventsChanged(),CancellationToken.None);
            return Results.Ok(result);
        })).WithMetadata(permission);
    }
    private static string Secret(HttpContext c)=>c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<EventChange> Read(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is >32768) throw new ArgumentException("活动请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly:false } limit) limit.MaxRequestBodySize=32768;
        return await c.Request.ReadFromJsonAsync<EventChange>(Json,c.RequestAborted) ?? throw new ArgumentException("活动字段缺失。");
    }
}
