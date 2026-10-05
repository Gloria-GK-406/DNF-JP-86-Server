using System.Text.Json.Serialization;
namespace Dfo.Server.Data.Inventory;

public sealed record CustomDailyRefill(int ItemId, int Quantity, int Mode, DateTimeOffset? ExpiresAt);
public sealed record DailyRefillSettings(CustomDailyRefill[] Custom, int[] DisabledPvf);
public sealed record DailyRefillEntry(int ItemId, string Name, string Source, int Quantity, int Mode,
    DateTimeOffset? ExpiresAt, string Status, bool PvfEnabled);
public sealed record DailyRefillList(long Revision, DailyRefillEntry[] Entries, DateTimeOffset? UpdatedAt, string? UpdatedBy);
public sealed record DailyRefillChange([property: JsonRequired] long ExpectedRevision, [property: JsonRequired] string Action,
    [property: JsonRequired] int ItemId, int? Quantity = null, int? Mode = null, DateTimeOffset? ExpiresAt = null, bool? Enabled = null);
