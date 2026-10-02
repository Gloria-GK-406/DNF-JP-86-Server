using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.HttpApi;

public interface IHttpApiFeature
{
    bool Enabled { get; }
    Uri? ListenUrl { get; }
    void ConfigureServices(IServiceCollection services);
    void ConfigureMiddleware(IApplicationBuilder application);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public sealed record HttpListenerScope(int Port);

internal sealed class HttpListenerBoundaryMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<HttpListenerScope>() is { } scope
            && context.Connection.LocalPort != scope.Port)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(context);
    }
}
