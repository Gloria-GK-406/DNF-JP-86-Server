using Dfo.Server.Application.Administration;
using Dfo.Server.Application.PlayerPortal;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.Tests.PlayerPortal;

/// <summary>What a player's mail must hold before the GM mail rules see it, how it maps, and its limits (#329, RQ-011, RQ-022).</summary>
public sealed class PlayerPortalMailRuleTests
{
    private readonly PlayerPortalRuleService rules = new();

    // Normalize is pure: it reads none of the content services the constructor takes.
    private static readonly GmMailRuleService GmRules = new(null!, null!, null!, null!);

    private static PlayerMailRequest Mail(string body = "", PlayerMailItem[]? items = null, int gold = 0, string title = "", int character = 11) =>
        new(Guid.NewGuid(), character, title, body, items ?? [], gold);

    [Fact]
    public void A_body_an_item_or_gold_is_enough()
    {
        rules.CheckMailContent(Mail(body: "正文"));
        rules.CheckMailContent(Mail(items: [new(3, 1)]));
        rules.CheckMailContent(Mail(gold: 1));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "   ")]
    [InlineData("只有标题", "")]
    [InlineData("只有标题", "\n\t")]
    public void A_mail_without_a_body_or_an_attachment_is_refused(string title, string body)
    {
        var refusal = Assert.Throws<PlayerPortalException>(() => rules.CheckMailContent(Mail(body, title: title)));
        Assert.Equal(PlayerPortalRefusal.Invalid, refusal.Reason);
        Assert.Equal("邮件至少要有正文或附件。", refusal.Message);
    }

    /// <summary>R-3: the GM rules would accept the empty mail by filling in their default body, so the player check must come first.</summary>
    [Fact]
    public void The_gm_rules_alone_would_let_an_empty_mail_through()
    {
        var empty = Mail();
        var normalized = GmRules.Normalize(rules.ToMailInput(empty), true);
        Assert.Equal("此为官方邮件，无需回复。", normalized.Body);
        Assert.Throws<PlayerPortalException>(() => rules.CheckMailContent(empty));
    }

    [Fact]
    public void Null_items_and_impossible_characters_are_invalid()
    {
        Assert.Equal(PlayerPortalRefusal.Invalid, Assert.Throws<PlayerPortalException>(() => rules.CheckMailContent(Mail("x", [null!]))).Reason);
        Assert.Equal(PlayerPortalRefusal.Invalid, Assert.Throws<PlayerPortalException>(() => rules.CheckMailContent(Mail("x", character: 0))).Reason);
        Assert.Equal(PlayerPortalRefusal.Invalid, Assert.Throws<PlayerPortalException>(() => rules.CheckMailContent(Mail("x") with { RequestId = Guid.Empty })).Reason);
    }

    [Fact]
    public void The_mail_input_names_the_one_recipient_and_never_equipment_options()
    {
        var input = rules.ToMailInput(Mail("b", [new(27312, 1), new(3, 2)], 5, "t", 13));
        Assert.Equal([13], input.RecipientIds);
        Assert.Equal([new GmMailItem(27312, 1), new GmMailItem(3, 2)], input.Items);
        Assert.All(input.Items, item => Assert.Null(item.Equipment));
        Assert.Equal(("t", "b", 5), (input.Title, input.Body, input.Gold));
    }

    [Fact]
    public void The_payload_hash_changes_with_any_part_of_the_content()
    {
        GmMailInput Input(int character = 11, string title = "t", string body = "b", int gold = 5, params GmMailItem[] items) =>
            new(title, body, [character], items.Length == 0 ? [new(3, 1), new(27312, 1)] : items, gold);
        var hash = rules.MailPayloadHash(Input());
        Assert.Equal(hash, rules.MailPayloadHash(Input()));
        foreach (var other in new[] { Input(character: 12), Input(title: "u"), Input(body: "c"), Input(gold: 6),
            Input(items: [new(27312, 1), new(3, 1)]), Input(items: [new(3, 2), new(27312, 1)]) })
            Assert.NotEqual(hash, rules.MailPayloadHash(other));
        Assert.NotEqual(rules.CeraPayloadHash("Cera", 5), hash);
    }

    [Fact]
    public void Zero_limits_never_refuse()
    {
        rules.CheckMailLimits(PlayerPortalLimits.None, int.MaxValue, long.MaxValue - 1);
    }

    [Fact]
    public void Gold_per_mail_and_mails_per_day_allow_exactly_the_limit()
    {
        var limits = PlayerPortalLimits.None with { MailGoldPerMail = 500, MailsPerDay = 2 };
        rules.CheckMailLimits(limits, 500, 1);
        Assert.Equal(PlayerPortalRefusal.LimitExceeded, Assert.Throws<PlayerPortalException>(() => rules.CheckMailLimits(limits, 501, 0)).Reason);
        Assert.Equal(PlayerPortalRefusal.LimitExceeded, Assert.Throws<PlayerPortalException>(() => rules.CheckMailLimits(limits, 0, 2)).Reason);
    }
}
