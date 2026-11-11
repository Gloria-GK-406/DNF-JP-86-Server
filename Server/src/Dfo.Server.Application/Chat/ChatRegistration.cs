using Dfo.Server.Abstractions.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Chat;

/// <summary>What the Chat domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class ChatRegistration
{
    internal static IServiceCollection AddChat(this IServiceCollection services)
    {
        services.TryAddSingleton<IChatScopeDomainService, ChatScopeDomainService>();
        return services;
    }
}
