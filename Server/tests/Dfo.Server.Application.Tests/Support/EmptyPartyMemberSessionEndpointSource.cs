using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Tests.Support;

internal sealed class EmptyPartyMemberSessionEndpointSource : IPartyMemberSessionEndpointSource
{
    internal static EmptyPartyMemberSessionEndpointSource Instance { get; } = new();

    public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
}
