using System.Text;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRedPacketDomainService(IGuildDomainStore guilds, IGuildRedPacketStore store,
    GuildRedPacketRuleService source, ICharacterDomainService characters, ICharacterBusinessInventoryDomainService inventory,
    IApplyEffectsDomainService effects, IAccountWalletDomainService wallets, IMailboxDomainService mailbox, TimeProvider clock)
    : IGuildRedPacketDomainService
{
    public async ValueTask<GuildRedPacketList?> ReadAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (source.Read() is null ||
            await guilds.FindMembershipAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { } membership)
            return null;
        var guild = membership.Guild.Id;
        var now = clock.GetLocalNow();
        var day = Day(now);
        var taken = await store.CountTakenAsync(actor.AccountId, day, ct).ConfigureAwait(false);
        var packets = await store.ListAsync(guild, now.ToUnixTimeSeconds(), ct).ConfigureAwait(false);
        var members = await guilds.ReadRosterCharacterIdsAsync(actor.CharacterId, ct).ConfigureAwait(false);
        return new(members.Count, taken, packets);
    }

    public async ValueTask<IReadOnlyList<GuildRedPacketClaim>> ReadHistoryAsync(CharacterId actor, uint packet, CancellationToken ct) =>
        await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is { } membership &&
        await store.FindAsync(packet, membership.Guild.Id, ct).ConfigureAwait(false) is not null
            ? await store.HistoryAsync(packet, ct).ConfigureAwait(false) : [];

    public async ValueTask<int> CountAvailableAsync(CharacterId character, AccountId account, CancellationToken ct)
    {
        var now = clock.GetLocalNow();
        if (source.Read() is not { } rules ||
            await guilds.FindMembershipAsync(character, ct).ConfigureAwait(false) is not { } membership ||
            await store.CountTakenAsync(account, Day(now), ct).ConfigureAwait(false) >= rules.DailyTakeLimit)
            return 0;
        var packets = await store.ListAsync(membership.Guild.Id, now.ToUnixTimeSeconds(), ct).ConfigureAwait(false);
        var taken = await store.TakenPacketsAsync(membership.Guild.Id, account, ct).ConfigureAwait(false);
        return packets.Count(packet => packet.IsOpenAt(now.ToUnixTimeSeconds()) &&
            packet.SenderAccount != account && !taken.Contains(packet.Id));
    }

    public async ValueTask<GuildRedPacketResult> SendAsync(SelectedSessionIdentity actor, int selection, byte shares,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (source.Read() is not { } rules ||
            await guilds.FindMembershipAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildRedPacketStatus.Unavailable);

        var now = clock.GetLocalNow();
        var guild = membership.Guild.Id;
        var activeCount = await store.CountActiveAsync(guild, now.ToUnixTimeSeconds(), ct).ConfigureAwait(false);
        var issuance = GuildRedPacketRuleService.EvaluateIssuance(rules, selection, shares, activeCount, now);
        if (issuance.Product is not { } product)
            return new(issuance.Status);
        var itemId = issuance.ItemId;
        var bags = inventory.GetStore(actor.CharacterId, BusinessInventoryStoreKind.Item);
        ItemReference? held = null;
        foreach (var space in new[] { BusinessInventorySpace.Consumable, BusinessInventorySpace.QuickSlot })
        {
            if (bags.FindContainer(space) is not { } bag)
                continue;
            held = (await bag.GetAllAsync(ct).ConfigureAwait(false)).FirstOrDefault(item =>
                item.ItemId == itemId && item.Count > 0 && !item.Snapshot.Lifetime.IsExpired(now));
            if (held is not null)
                break;
        }
        if (held?.BusinessSlot is not { } slot)
            return new(GuildRedPacketStatus.Invalid);
        var name = await NameAsync(actor.CharacterId, ct).ConfigureAwait(false);
        await effects.ApplyAsync(new(actor.AccountId, actor.CharacterId, now, footprint, actor),
            [new ConsumeFromContainerEffect(slot, itemId, 1)], ct).ConfigureAwait(false);
        await store.AddAsync(new(0, guild, actor.CharacterId, actor.AccountId, name, itemId, product.Price, product.ContributionRate,
            shares, product.Price, shares, now.ToUnixTimeSeconds(), issuance.ExpiresAt, false, null, 0), ct)
            .ConfigureAwait(false);
        return new(GuildRedPacketStatus.Success, product.Price, itemId, slot, held.Count - 1, guild);
    }

    public async ValueTask<GuildRedPacketResult> TakeAsync(SelectedSessionIdentity actor, uint id, CancellationToken ct)
    {
        if (source.Read() is not { } rules ||
            await guilds.FindMembershipAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { } membership)
            return new(GuildRedPacketStatus.Unavailable);
        var now = clock.GetLocalNow();
        var timestamp = now.ToUnixTimeSeconds();
        if (await store.FindAsync(id, membership.Guild.Id, ct).ConfigureAwait(false) is not { } packet ||
            !packet.IsOpenAt(timestamp))
            return new(GuildRedPacketStatus.Gone);
        if (packet.SenderAccount == actor.AccountId || await store.HasTakenAsync(id, actor.AccountId, ct).ConfigureAwait(false))
            return new(GuildRedPacketStatus.AlreadyTaken);
        var day = Day(now);
        if (await store.CountTakenAsync(actor.AccountId, day, ct).ConfigureAwait(false) >= rules.DailyTakeLimit)
            return new(GuildRedPacketStatus.DailyTakeLimit);
        var amount = GuildRedPacketRuleService.RollClaimAmount(packet.Remaining, packet.SharesLeft, Random.Shared.Next());
        try
        {
            await wallets.AddAsync(actor.AccountId, AccountCurrency.HappyTokenCera, amount, ct).ConfigureAwait(false);
        }
        catch (WalletException e) when (e.Rejection == WalletRejection.BalanceLimit)
        {
            return new(GuildRedPacketStatus.BalanceLimit);
        }
        var name = await NameAsync(actor.CharacterId, ct).ConfigureAwait(false);
        if (!await store.TakeAsync(packet, new(packet.Id, actor.AccountId, actor.CharacterId, name, amount, timestamp), day, ct)
            .ConfigureAwait(false))
            throw new InvalidOperationException("The guild red packet changed during its wallet transaction.");
        if (packet.SharesLeft == 1)
            await SettleAsync(packet with { Remaining = 0, SharesLeft = 0 }, now, ct).ConfigureAwait(false);
        return new(GuildRedPacketStatus.Success, amount, packet.ItemId, Guild: packet.Guild);
    }

    public async ValueTask ExpireAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await store.FindDueAsync(now.ToUnixTimeSeconds(), 64, ct).ConfigureAwait(false);
        foreach (var packet in due)
            await SettleAsync(packet, now, ct).ConfigureAwait(false);
    }

    private async ValueTask SettleAsync(GuildRedPacket packet, DateTimeOffset now, CancellationToken ct)
    {
        var (refund, points) = GuildRedPacketRuleService.CalculateSettlement(
            packet.Amount, packet.Remaining, packet.ContributionRate, packet.SenderDeparted);
        if (refund > 0)
            await mailbox.SendAsync(MailBuilder.FromSystem(packet.Sender)
                .WithText("公会红包退款", $"红包到期，未领取金额 {refund} 已退为代币券。")
                .WithDeliveryKey($"guild-red-packet:{packet.Id}:refund").AddTokenCera(refund).Build(), ct).ConfigureAwait(false);
        if (points > 0)
            await mailbox.SendAsync(MailBuilder.FromSystem(packet.Sender)
                .WithText("公会红包贡献奖励", $"赠发红包获得 {points} 点公会贡献。")
                .WithDeliveryKey($"guild-red-packet:{packet.Id}:contribution").ExpiresAt(now.AddDays(15))
                .AddStackable(GuildRedPacketRules.ContributionCoin, points).Build(), ct).ConfigureAwait(false);
        await store.SettleAsync(packet, now.ToUnixTimeSeconds(), refund, points, ct).ConfigureAwait(false);
    }

    private async ValueTask<string> NameAsync(CharacterId character, CancellationToken ct) =>
        await characters.Get(character).IdentityAsync(ct).ConfigureAwait(false) is { } identity
            ? Encoding.UTF8.GetString(identity.Name) : throw new InvalidOperationException("Guild member identity is missing.");

    internal static int Day(DateTimeOffset now) => DateOnly.FromDateTime(now.DateTime.AddHours(-6)).DayNumber;
}
