namespace Dfo.Server.Abstractions.Content;

/// <summary>Admission metadata independent of a maze or specialized run structure.</summary>
public sealed record DungeonAdmissionDefinition(int DungeonId, string ArchivePath, byte MinimumRequiredLevel,
    bool ConsumesFatigue, IReadOnlyList<byte>? SupportedDifficulties, bool HasStoryMode,
    IReadOnlyList<DungeonRequiredItem> RequiredItems, IReadOnlyList<DungeonRequiredItem> EventRequiredItems,
    bool ProhibitsPractice, int? PartyLimit)
{
    public static DungeonAdmissionDefinition From(DungeonDefinition d) => new(d.DungeonId, d.ArchivePath,
        d.MinimumRequiredLevel, d.ConsumesFatigue, d.SupportedDifficulties, d.HasStoryMode,
        d.RequiredItems, d.EventRequiredItems, d.ProhibitsPractice, d.PartyLimit);
}
public interface IDungeonAdmissionDefinitionSource
{
    ValueTask<DungeonAdmissionDefinition?> GetAdmissionAsync(int dungeonId, CancellationToken ct);
}
public interface IDungeonAdmissionRuleService
{
    ValueTask<DungeonAdmissionDefinition?> GetAsync(int dungeonId, CancellationToken ct);
}
