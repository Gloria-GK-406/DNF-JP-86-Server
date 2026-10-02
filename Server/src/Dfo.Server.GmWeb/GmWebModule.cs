using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;

namespace Dfo.Server.GmWeb;

public sealed class GmWebModule(IConfiguration configuration) : IHttpApiFeature
{
    private readonly GmWebOptions options = GmWebOptions.FromConfiguration(configuration);
    public bool Enabled => options.Enabled;
    public Uri? ListenUrl => options.ListenUrl;
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(options);
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(options => { options.HeaderName = "X-GM-CSRF"; options.Cookie.Name = "Dfo.Gm.Csrf"; options.Cookie.SameSite = SameSiteMode.Strict; options.Cookie.HttpOnly = true; });
        services.AddSingleton<GmLoginLimiter>();
        services.AddHostedService<GmBootstrapService>();
    }
    public void ConfigureMiddleware(IApplicationBuilder application) => application.UseMiddleware<GmAccessBoundary>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("").WithMetadata(new HttpListenerScope(options.ListenUrl!.Port));
        group.MapGet("/api/gm/v1/health", () => Results.Ok(new { status = "ready" })).WithMetadata(new GmPublicEndpoint());
        GmAuthenticationEndpoints.Map(group);
        GmPlayerDirectoryEndpoints.Map(group);
        group.MapGet("/gm/{**path}", ServePage);
    }

    private static IResult ServePage(HttpContext context, string? path)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "gm"));
        var name = string.IsNullOrEmpty(path) ? "index.html" : path;
        var candidate = Path.GetFullPath(Path.Combine(root, name));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return Results.NotFound();
        if (!File.Exists(candidate))
        {
            if (Path.HasExtension(name) || name.StartsWith("assets/", StringComparison.Ordinal))
                return Results.NotFound();
            candidate = Path.Combine(root, "index.html");
        }
        if (!File.Exists(candidate)) return Results.Problem("GM 页面尚未构建。", statusCode: 503);
        context.Response.Headers.CacheControl = Path.GetFileName(candidate) == "index.html"
            ? "no-cache" : "public,max-age=31536000,immutable";
        new FileExtensionContentTypeProvider().TryGetContentType(candidate, out var contentType);
        return Results.File(candidate, contentType ?? "application/octet-stream");
    }
}
