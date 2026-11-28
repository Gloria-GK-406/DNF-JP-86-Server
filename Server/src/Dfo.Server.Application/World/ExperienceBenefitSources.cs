using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal readonly record struct DungeonExperienceBenefitContext(ExperienceOccasion Occasion, int ListenerPort);

internal static class DungeonExperienceBenefits
{
    public static GameplayBenefit Entry(string key, string name, int order, ExperienceEvent occasion,
        double rate, ExperienceBonusKind kind, DateTimeOffset? endsAt = null) =>
        new(key, name, order, new ExperienceBenefit(occasion, new(name, rate, kind)), EndsAt: endsAt);
}

internal sealed class PremiumExperienceBenefits(IPremiumServiceDomainService premiums,
    IPremiumServiceRuleService rules, ExperienceEntitlementPolicy entitlements)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var account = premiums.For(occasion.Account);
        var entries = await account.ReadAtAsync(at, ct).ConfigureAwait(false);
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        ExperienceBonusRules.FromAccountEntitlements(entries, at, occasion.Event, entitlements, found);
        var growth = entries.SingleOrDefault(item => item.PremiumType == (int)PremiumServiceType.GrowthContract);
        if (occasion.Event is ExperienceEvent.MonsterKill or ExperienceEvent.DungeonClear
            && growth is { } term && term.ExpiresAtUnixSeconds > at.ToUnixTimeSeconds() && rules.GrowthExperienceBonusRate > 0)
            found.Add(DungeonExperienceBenefits.Entry("growth-contract", "growth contract", 200, occasion.Event,
                rules.GrowthExperienceBonusRate, ExperienceBonusKind.GrowthContract,
                DateTimeOffset.FromUnixTimeSeconds(term.ExpiresAtUnixSeconds)));
        if (occasion.Event == ExperienceEvent.DungeonClear
            && await account.GetBlackDiamondExperienceBonusRateAsync(ct).ConfigureAwait(false) is > 0 and var blackDiamond)
            found.Add(DungeonExperienceBenefits.Entry("black-diamond", "black diamond", 400, occasion.Event,
                blackDiamond, ExperienceBonusKind.BlackDiamond));
        return new("premium-services", GameplayBenefitScope.For(occasion.Account), found.ToImmutable());
    }
}

internal sealed class ChannelExperienceBenefits(IChannelEntryDomainService channels, IChannelExperienceSource rates)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var channel = await channels.ResolveAsync(context.ListenerPort, ct).ConfigureAwait(false);
        var rate = rates.Load().RateFor(channel.ChannelId, occasion.DungeonId);
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        ExperienceBonusRules.FromChannel(occasion.Event, rate, found);
        return new("channel", GameplayBenefitScope.For(occasion.Character), found.ToImmutable());
    }
}

internal sealed class GuildExperienceBenefits(IGuildBenefitDomainService guilds)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var rate = occasion.Event == ExperienceEvent.MonsterKill && occasion.AllowGuildExperience
            ? await guilds.ReadMonsterExperienceRateAsync(occasion.Character, ct).ConfigureAwait(false) : 0;
        return new("guild", GameplayBenefitScope.For(occasion.Character),
            rate > 0 ? [DungeonExperienceBenefits.Entry("experience", "guild", 300, occasion.Event, rate, ExperienceBonusKind.GuildReward)] : []);
    }
}

internal sealed class SpiritPotionExperienceBenefits(IOnlineCharacterRuntimeDomainService runtime)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var potion = runtime.SpiritPotionOf(occasion.Character);
        // 70Reference: the timed potion affects settlement only.
        return ValueTask.FromResult(new GameplayBenefitBatch("spirit-potion", GameplayBenefitScope.For(occasion.Character),
            occasion.Event == ExperienceEvent.DungeonClear && potion is { } active
                ? [DungeonExperienceBenefits.Entry("experience", "spirit potion", 500, occasion.Event, active.Rate,
                    ExperienceBonusKind.SpiritPotion, active.ExpiresAt)] : []));
    }
}

internal sealed class AdventureGroupExperienceBenefits(IAdventureGroupDomainService groups)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var rate = occasion.Event == ExperienceEvent.DungeonClear
            ? await groups.ClearExperienceRateAsync(occasion.Account, occasion.Character, ct).ConfigureAwait(false) : 0;
        return new("adventure-group", GameplayBenefitScope.For(occasion.Character),
            rate > 0 ? [DungeonExperienceBenefits.Entry("experience", "adventure group", 600, occasion.Event, rate,
                ExperienceBonusKind.AdventureGroup)] : []);
    }
}

internal sealed class RunExperienceBenefits(IDungeonRunQueryDomainService runs)
    : IGameplayBenefitSource<DungeonExperienceBenefitContext>
{
    public ValueTask<GameplayBenefitBatch> DescribeAsync(
        DungeonExperienceBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var occasion = context.Occasion;
        var found = ImmutableArray.CreateBuilder<GameplayBenefit>();
        if (occasion.RunId is { } runId && runs.Find(occasion.Character) is
            { Layout.Mode.Kind: DungeonRunKind.Ordinary, Participant: { } participant } run && run.RunId == runId)
        {
            if (participant.IsLuckyCharacter)
                found.Add(DungeonExperienceBenefits.Entry("lucky-character", "lucky character", 700, occasion.Event,
                    occasion.Event == ExperienceEvent.DungeonClear ? 0.15 : 0.10, ExperienceBonusKind.LuckyCharacter));
            if (occasion.Event == ExperienceEvent.DungeonClear
                && run.Participants.Any(member => member.CharacterId != occasion.Character && member.IsLuckyCharacter
                    && run.ClearParticipants.Any(eligible => eligible.CharacterId == member.CharacterId)))
                found.Add(DungeonExperienceBenefits.Entry("lucky-teammate", "lucky character party", 800, occasion.Event,
                    0.10, ExperienceBonusKind.LuckyCharacterParty));
            if (participant.BurnsFatigue && (occasion.Event == ExperienceEvent.DungeonClear
                || run.Layout.EntryOnlyFatigue is not null || run.CurrentCellState.FatigueBurnedBy.Contains(occasion.Character)))
                found.Add(DungeonExperienceBenefits.Entry("fatigue-burning", "fatigue burning", 900, occasion.Event,
                    1.0, ExperienceBonusKind.FatigueBurn));
        }
        return ValueTask.FromResult(new GameplayBenefitBatch("dungeon-run", GameplayBenefitScope.For(occasion.Character),
            found.ToImmutable()));
    }
}
