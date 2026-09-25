namespace Dfo.Server.Protocol.Game.Mailbox;

internal static class MailboxIds
{
    private const uint AttachmentBase = 0x40000000;
    public static uint Attachment(long id) => id > 0 && id < AttachmentBase
        ? checked(AttachmentBase + (uint)id) : throw new InvalidDataException("Mail attachment ID exceeds wire range.");
    public static long DecodeAttachment(uint wire) => wire > AttachmentBase && wire < 2 * AttachmentBase
        ? wire - AttachmentBase : throw new InvalidDataException("Not an attachment ID.");
}
