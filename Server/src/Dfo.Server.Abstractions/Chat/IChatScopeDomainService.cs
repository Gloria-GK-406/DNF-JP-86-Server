using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Chat;

/// <summary>Answers who is in a chat scope right now.</summary>
public interface IChatScopeDomainService
{
    /// <summary>The characters in the world that the scope reaches, or none when it reaches no one.</summary>
    /// <exception cref="NotSupportedException">The scope is a shape this service was not taught.</exception>
    IReadOnlyList<CharacterId> MembersOf(ChatScope scope);
}
