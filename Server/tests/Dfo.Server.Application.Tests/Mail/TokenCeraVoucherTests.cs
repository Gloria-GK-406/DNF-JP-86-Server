using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dfo.Server.Application.Tests.Mail;

/// <summary>The consignment seller's token Cera voucher: kept in mail, credited once when claimed.</summary>
public sealed class TokenCeraVoucherTests
{
    [Fact]
    public async Task A_voucher_credits_token_cera_once_when_claimed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture();
            var mail = TestUseCase.Mailbox(f.Services);
            var letter = MailBuilder.FromSystem(new CharacterId(1)).WithText("拍卖行", "成交").AddTokenCera(95).Build();
            _ = await mail.SendPreparedAsync(letter, [new PreparedMailAttachment(letter.Attachments[0])]);

            var attachment = Assert.Single(Assert.Single(await mail.InboxAsync(new CharacterId(1))).Attachments);
            Assert.Equal(new MailAsset.TokenCera(95), attachment.Asset);
            Assert.Equal(0, await TokenCeraAsync(f));

            var receipt = await mail.ClaimAsync(new CharacterId(1), attachment.Id);
            Assert.True(receipt.AccountCurrencyChanged);
            Assert.Equal(95, await TokenCeraAsync(f));
            var again = await Assert.ThrowsAsync<MailException>(() => mail.ClaimAsync(new CharacterId(1), attachment.Id).AsTask());
            Assert.Equal(MailFailure.AlreadyClaimed, again.Reason);
            Assert.Equal(95, await TokenCeraAsync(f));
        });
    }

    [Fact]
    public void A_player_cannot_mail_a_voucher() =>
        Assert.Throws<MailException>(() =>
            MailBuilder.FromCharacter(new CharacterId(1), new CharacterId(2)).AddTokenCera(10).Build());

    private static async Task<int> TokenCeraAsync(ShopFixture f) =>
        (await f.Services.GetRequiredService<IAccountWalletDomainService>().ReadAsync(new AccountId(1), default))!.TokenCera;
}
