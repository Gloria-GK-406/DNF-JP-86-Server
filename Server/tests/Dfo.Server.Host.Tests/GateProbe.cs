using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// The gate as the tests' launcher sees it: a client carrying the token, and the readiness probe that
/// replaced the retired bootstrap route.
/// </summary>
internal static class GateProbe
{
    public const string Register = "/api/gate/v1/register";
    public const string Login = "/api/gate/v1/login";
    public const string Status = "/api/gate/v1/status";

    public static HttpClient Client(Uri baseAddress, string token)
    {
        var client = new HttpClient { BaseAddress = baseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Posts an empty credential body until the gate answers 400 <c>invalid_request</c>: the listener is
    /// up, the token passed and the route is ready. 503 (and a refused connection) means not yet; any
    /// other answer fails at once with its status. Register is ready with HTTP alone, login also needs
    /// the game service. The status route is no probe here: without an archive digest it stays 503.
    /// </summary>
    /// <param name="exited">The Host's exit diagnostics once it has exited, else null.</param>
    public static async Task WaitUntilServedAsync(HttpClient client, string path, Func<string?> exited, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (exited() is { } diagnostics) throw new Xunit.Sdk.XunitException(diagnostics);
            try
            {
                using var response = await PostEmptyAsync(client, path, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    Assert.Equal("invalid_request", Code(body));
                    return;
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                    throw new Xunit.Sdk.XunitException($"The gate readiness probe {path} answered {(int)response.StatusCode}: {body}");
            }
            catch (HttpRequestException)
            {
                // Cold start: the listener is not bound yet.
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    public static async Task<HttpResponseMessage> PostEmptyAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await client.PostAsync(path, content, cancellationToken);
    }

    public static string? Code(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("code").GetString();
    }
}
