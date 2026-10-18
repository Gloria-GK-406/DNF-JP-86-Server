using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Marriage;
public interface IMarriageGuestbookDomainStore
{
    ValueTask<IReadOnlyList<MarriageGuestComment>> ReadAsync(Guid relation, CancellationToken ct);
    ValueTask AddAsync(Guid relation, CharacterId author, byte[] name, byte[] text, uint sortKey, CancellationToken ct);
    ValueTask<bool> DeleteAsync(Guid relation, uint id, CancellationToken ct);
}
public interface IMarriageGuestbookRuleService { int MaximumBytes { get; } int MaximumComments { get; } }
