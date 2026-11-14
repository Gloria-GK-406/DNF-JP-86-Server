using System.Text.Json;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>
/// How player receipts are stored and read back. They are written with the web naming the API answers in, and every
/// answer (the grant, its replay, the record page) parses the one stored text, so a replay is the original result.
/// </summary>
internal static class PlayerPortalReceipts
{
    internal const string Cera = "cera";
    internal const string Mail = "mail";
    internal const string Succeeded = "成功";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string Serialize<T>(T receipt) where T : notnull => JsonSerializer.Serialize(receipt, Options);

    internal static JsonElement Parse(string stored) => JsonSerializer.Deserialize<JsonElement>(stored, Options);
}
