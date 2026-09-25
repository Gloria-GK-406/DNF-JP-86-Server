using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Mail;

public static class MailboxProjection
{
    public static CharacterMailboxEntryState Map(CharacterMailboxStorageRecord storage) =>
new CharacterMailboxEntryState(
                Math.Max(0, storage.TotalCount - storage.PageMessages.Count),
                storage.PageMessages.OrderByDescending(value => value.MessageId)
                    .Select(value => new CharacterMailboxMessageEntryState(
                        value.MessageId,
                        value.SenderCharacterId,
                        System.Text.Encoding.UTF8.GetBytes(value.SenderName),
                        value.MailType,
                        value.SourceProtocol,
                        System.Text.Encoding.UTF8.GetBytes(value.Title),
                        System.Text.Encoding.UTF8.GetBytes(value.Body),
                        value.Gold,
                        value.RemainingSeconds,
                        value.CreatedAtUnixSeconds,
                        checked((byte)value.LetterState),
                        value.Attachments.OrderBy(attachment => attachment.Ordinal)
                            .Select(attachment => new CharacterMailboxAttachmentEntryState(
                                attachment.AttachmentId,
                                attachment.Ordinal,
                                checked((byte)attachment.ItemType),
                                attachment.SourceListType,
                                attachment.SourceSlotIndex,
                                attachment.SourceItemUid,
                                attachment.ItemTemplateId,
                                attachment.ItemKind,
                                attachment.ItemCount,
                                attachment.InstanceValue,
                                attachment.Durability,
                                attachment.SealFlag,
                                attachment.OptionValue,
                                attachment.ExpireTime,
                                attachment.Marker16,
                                attachment.PetSerialOrHandle,
                                [.. attachment.ItemCore],
                                attachment.DetailJson))
                            .ToArray()) { GoldAttachments = value.GoldAttachments })
                    .ToArray());
}
