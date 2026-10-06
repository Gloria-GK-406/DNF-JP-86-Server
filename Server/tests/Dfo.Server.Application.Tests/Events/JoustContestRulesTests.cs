using Dfo.Server.Application.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Tests.Events;

public sealed class JoustContestRulesTests
{
    private static JoustKnight[] Knights => Enumerable.Range(0,8).Select(i=>new JoustKnight(i,"knight",0,[],[])).ToArray();

    [Theory]
    [InlineData(0,16)]
    [InlineData(.05,8)]
    [InlineData(.125,2)]
    [InlineData(1,1)]
    public void Price_curve_passes_the_four_accepted_anchors(double probability,decimal expected)
        => Assert.Equal(expected,JoustContestRules.BettingOdds(probability,0));

    [Fact]
    public void Curve_and_betting_are_bounded_monotone_and_high_probability_side_is_linear()
    {
        var previous=16m;
        for(var i=0;i<=10000;i++)
        {
            var current=JoustContestRules.InitialOdds(i/10000d);
            Assert.InRange(current,1m,16m);Assert.True(current<=previous);previous=current;
        }
        var a=JoustContestRules.InitialOdds(.25);var b=JoustContestRules.InitialOdds(.5);var c=JoustContestRules.InitialOdds(.75);
        Assert.InRange(Math.Abs((a-b)-(b-c)),0m,.000000000001m);
        Assert.Equal(1.5m,JoustContestRules.BettingOdds(.125,1000));
        Assert.Equal(1.1m,JoustContestRules.BettingOdds(.125,9000));
        Assert.Equal(1m,JoustContestRules.BettingOdds(1,0));Assert.Equal(1m,JoustContestRules.BettingOdds(1,long.MaxValue));
        Assert.Equal(1m,JoustContestRules.BettingOdds(0,long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(()=>JoustContestRules.BettingOdds(.125,-1));
        foreach(var invalid in new[]{double.NaN,double.PositiveInfinity,-.1,1.1})
            Assert.Throws<ArgumentOutOfRangeException>(()=>JoustContestRules.InitialOdds(invalid));
    }

    [Fact]
    public void Equal_knights_without_history_have_one_eighth_chance_and_two_times_odds()
    {
        var forecast=JoustContestRules.Forecast(Knights,Enumerable.Repeat((byte)1,8).ToArray(),[]);
        Assert.All(forecast,f=>
        {
            Assert.Equal(.125,f.HistoricalProbability);Assert.Equal(.125,f.ChampionshipProbability);
            Assert.Equal(.125,f.PricingProbability);Assert.Equal(2m,JoustContestRules.BettingOdds(f.PricingProbability,0));
        });
    }

    [Theory]
    [InlineData(0,1)] [InlineData(1,27)] [InlineData(27,28)] [InlineData(28,0)]
    public void Counter_cycle_has_reciprocal_advantage_and_complementary_match_probabilities(int left,int right)
    {
        var a=new JoustKnight(0,"a",left,[],[]);var b=new JoustKnight(1,"b",right,[],[]);
        Assert.Equal(1.25,JoustContestRules.AttackAdvantage(left,right));Assert.Equal(.8,JoustContestRules.AttackAdvantage(right,left));
        var chance=JoustContestRules.MatchWinProbability(a,1,b,1);
        Assert.True(chance>.5);Assert.Equal(1-chance,JoustContestRules.MatchWinProbability(b,1,a,1),12);
        Assert.True(JoustContestRules.MatchWinProbability(a,0,b,2)>chance);
        Assert.Equal(.5,JoustContestRules.MatchWinProbability(a,1,a,1));
        Assert.Equal(1,JoustContestRules.AttackAdvantage(0,27));Assert.Equal(1,JoustContestRules.AttackAdvantage(999,right));
    }

    [Fact]
    public void Forecast_integrates_every_possible_opponent_and_neutral_current_probability_does_not_dilute_history()
    {
        var forecast=JoustContestRules.Forecast(Knights,Enumerable.Repeat((byte)1,8).ToArray(),[new(0,100,0,33)]);
        Assert.Equal(Math.Pow(11d/12,3),forecast[0].HistoricalProbability,12);
        Assert.All(forecast,f=>Assert.Equal(f.HistoricalProbability,f.PricingProbability));
        Assert.True(JoustContestRules.InitialOdds(forecast[0].PricingProbability)<2);
        var types=new[]{0,1,27,28,0,1,27,28};
        var mixed=JoustContestRules.Forecast(Knights.Select((k,i)=>k with{AttackType=types[i]}).ToArray(),[0,1,2,0,1,2,0,1],[new(0,100,0,33)]);
        foreach(var distribution in new[]{mixed.Select(f=>f.HistoricalProbability),mixed.Select(f=>f.ChampionshipProbability),mixed.Select(f=>f.PricingProbability)})
        {Assert.Equal(1,distribution.Sum(),12);Assert.All(distribution,p=>Assert.InRange(p,0,1));}
    }

    [Fact]
    public void Common_probability_weight_keeps_neutral_knights_unchanged_even_at_extreme_history()
    {
        double[] history=[.999,.0001,.00015,.00015,.00015,.00015,.00015,.00015];
        double[] current=[.125,.01,.24,.125,.125,.125,.125,.125];
        var prices=JoustContestRules.PricingProbabilities(history,current);
        Assert.Equal(history[0],prices[0]);Assert.Equal(history[3],prices[3]);
        Assert.All(prices,p=>Assert.InRange(p,0,1));Assert.Equal(1,prices.Sum(),12);
    }

    [Fact]
    public void Seeded_results_are_reproducible_and_good_condition_wins_more_often_than_bad_condition()
    {
        var rules=new JoustRuleService(null!);
        var definition=new JoustDefinition("fixture",17,1000,[],new(1,"reward",null),new(2,"participation",null),
            Enumerable.Range(0,12).Select(i=>new JoustKnight(i,"knight",0,[],[])).ToArray());
        var start=DateTimeOffset.UnixEpoch;var schedule=new JoustScheduleConfiguration(0,JoustSchedule.Default);
        var first=rules.CreateRound("test",start,schedule,definition,seed:42);
        var repeated=rules.CreateRound("test",start,schedule,definition,seed:42);
        Assert.Equal(first.Knights,repeated.Knights);Assert.Equal(first.Forecasts,repeated.Forecasts);Assert.Equal(first.Winners,repeated.Winners);
        var matches=0;var wins=0;
        for(var seed=0;seed<4000;seed++)
        {
            var round=rules.CreateRound("test",start,schedule,definition,seed:seed);
            _=rules.ResultStatistics(round);
            for(var i=0;i<4;i++)
            {
                var a=round.Forecasts[i*2];var b=round.Forecasts[i*2+1];
                if(a.Condition==0 && b.Condition==2){matches++;if(round.Winners[i]==a.KnightIndex)wins++;}
                if(a.Condition==2 && b.Condition==0){matches++;if(round.Winners[i]==b.KnightIndex)wins++;}
            }
        }
        Assert.True(matches>2000);Assert.InRange((double)wins/matches,.545,.605);
    }
}
