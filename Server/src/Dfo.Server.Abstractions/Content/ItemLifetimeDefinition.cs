namespace Dfo.Server.Abstractions.Content;

/// <summary>Template lifetime declarations; zero/false means no corresponding limit.</summary>
public readonly record struct ItemLifetimeDefinition(int AbsoluteExpiration, int Days, bool DailyDelete)
{
    public bool IsLimited => AbsoluteExpiration > 0 || Days > 0 || DailyDelete;
}
