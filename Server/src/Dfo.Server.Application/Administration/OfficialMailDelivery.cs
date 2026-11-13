using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Administration;

/// <summary>One delivered system mail: its ID and the UIDs of the equipment created for it.</summary>
internal sealed record OfficialMailDelivered(long MailId, int[] EquipmentUids);

/// <summary>
/// Builds one system mail for one recipient character, creates its attachment items and delivers it. The GM official
/// mail and the player's own mail (#329) share it, so both follow the same item, equipment and mailbox rules (SD-8).
/// It must run inside the command transaction: everything it creates commits or rolls back with the caller.
/// </summary>
internal sealed class OfficialMailDelivery(IItemLifecycleDomainService lifecycle, IMailboxDomainService mailbox, IGmMailPersistence persistence)
{
    /// <summary>How long a delivered official mail stays in the mailbox.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    internal async ValueTask<OfficialMailDelivered> DeliverAsync(CharacterId recipient, string title, string body,
        IReadOnlyList<GmMailPreparedItem> grants, int gold, DateTimeOffset expiresAt, CancellationToken token)
    {
        var builder = MailBuilder.FromSystem(recipient).WithText(title, body).ExpiresAt(expiresAt);
        var uids = new List<int>();
        var records = new List<ItemRecord>();
        foreach (var grant in grants)
        {
            if (grant.Preview.Kind == "equipment")
                for (var count = 0; count < grant.Requested.Count; count++)
                {
                    // The rule service already placed the tier's fixed attribute seed on the snapshot.
                    var record = await lifecycle.CreateAsync(grant.Snapshot, token);
                    var uid = record.ItemUid!.Value.Value;
                    records.Add(record); uids.Add(uid); builder.AddEquipment(new ItemIdentity(uid));
                }
            else
            {
                records.Add(await lifecycle.CreateAsync(grant.Snapshot, token));
                builder.AddStackable(grant.Requested.ItemId, grant.Requested.Count);
            }
        }
        if (gold > 0) builder.AddGold(gold);
        var mail = builder.Build();
        var prepared = mail.Attachments.Select((asset, index) => asset is MailAsset.Gold
            ? new PreparedMailAttachment(asset) : new PreparedMailAttachment(asset, [records[index]])).ToArray();
        var mailed = await mailbox.SendPreparedAsync(mail, prepared, token);
        await persistence.AssertMailWireRangeAsync(mailed.MailId, token);
        return new(mailed.MailId, uids.ToArray());
    }
}
