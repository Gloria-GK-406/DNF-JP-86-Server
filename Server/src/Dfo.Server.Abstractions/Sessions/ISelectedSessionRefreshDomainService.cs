using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

public enum SelectedSessionRefreshTopic { Altar, Dimension, GoldSilverHorn, ReviveCoin, Mirror, Heras, Tournament, ItemUsage, Premium, Fatigue }
public sealed record SelectedSessionRefreshScan(DateTimeOffset Period, ImmutableArray<SelectedSessionIdentity> Sessions);
public abstract record SelectedSessionRefreshState;
public sealed record PeriodRefreshState(DateTimeOffset Period) : SelectedSessionRefreshState;
public sealed record PremiumRefreshState(DateTimeOffset Period, string Active, bool CrystalActive, bool GrowthActive) : SelectedSessionRefreshState;
public sealed record FatigueRefreshState(CharacterFatigueBalance Balance) : SelectedSessionRefreshState;
public interface ISelectedSessionRefreshDomainService
{
    SelectedSessionRefreshScan Capture(SelectedSessionRefreshTopic topic);
    DateTimeOffset CurrentPeriod(SelectedSessionRefreshTopic topic);
    bool IsCurrent(SelectedSessionRefreshTopic topic, DateTimeOffset period, SelectedSessionIdentity session);
    SelectedSessionRefreshState? Previous(SelectedSessionRefreshTopic topic, SelectedSessionIdentity session);
    bool NeedsRefresh(SelectedSessionRefreshTopic topic, SelectedSessionIdentity session, SelectedSessionRefreshState state);
    bool MarkRegistered(SelectedSessionRefreshTopic topic, DateTimeOffset period, SelectedSessionIdentity session, SelectedSessionRefreshState state);
}
