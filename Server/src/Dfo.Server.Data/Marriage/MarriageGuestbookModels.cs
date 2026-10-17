using Dfo.Server.Data.Characters;
namespace Dfo.Server.Data.Marriage;
public sealed record MarriageGuestComment(uint Id, CharacterId Author, byte[] AuthorName, uint SortKey, byte[] Text);
public sealed record MarriageGuestbookResult(int Status, IReadOnlyList<MarriageGuestComment> Comments);
