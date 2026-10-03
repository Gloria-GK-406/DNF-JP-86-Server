using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Mail;

/// <summary>Item custody backed by a particular, persisted mail attachment in the same transaction.</summary>
public interface IMailItemCustodySession
{
    ValueTask AcceptMailAttachmentAsync(long attachmentId, IReadOnlyList<ItemRecord> records, CancellationToken ct);
    ValueTask<IReadOnlyList<ItemRecord>> TakeMailAttachmentAsync(CharacterId recipient, long attachmentId,
        DateTimeOffset now, CancellationToken ct);
}
