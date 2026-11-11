namespace Dfo.Server.HttpApi;

/// <summary>
/// The GM back office address a component contributes for the gate status <c>admin</c> field when
/// <c>LauncherAuth:AdminUrl</c> is not set. Absent from the container when no component serves one.
/// </summary>
public sealed record GateAdminAddress(string Url);
