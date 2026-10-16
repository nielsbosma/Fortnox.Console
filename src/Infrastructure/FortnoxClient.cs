using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fortnox.Console.Auth;

namespace Fortnox.Console.Infrastructure;

/// <summary>
/// HTTP against <c>https://api.fortnox.se/3/</c> as one account. Paths are relative to that base,
/// e.g. <c>customers/1001</c>.
///
/// Two retries are built in, because both failures are routine rather than exceptional: a 401 gets
/// one fresh token (the cached one may have been revoked or rotated away), and a 429 is retried
/// with backoff, since Fortnox allows only 25 requests per sliding 5 seconds and a paged
/// <c>--all</c> listing reaches that quickly.
/// </summary>
public sealed class FortnoxClient
{
    private const int MaxRateLimitRetries = 4;

    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.fortnox.se/3/") };

    private readonly ITokenSource _tokens;

    public FortnoxClient(ITokenSource tokens) => _tokens = tokens;

    public async Task<JsonDocument> GetAsync(string path) =>
        await ReadJson(await SendAsync(HttpMethod.Get, path, null));

    public async Task<JsonDocument> PostAsync(string path, string? json = null) =>
        await ReadJson(await SendAsync(HttpMethod.Post, path, json));

    public async Task<JsonDocument> PutAsync(string path, string? json = null) =>
        await ReadJson(await SendAsync(HttpMethod.Put, path, json));

    public async Task<JsonDocument> DeleteAsync(string path) =>
        await ReadJson(await SendAsync(HttpMethod.Delete, path, null));

    public async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, string? json) =>
        await ReadJson(await SendAsync(method, path, json));

    /// <summary>For endpoints that answer with a file, such as <c>invoices/{n}/print</c>.</summary>
    public async Task<(byte[] Bytes, string? ContentType)> GetBytesAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, "application/pdf, */*");
        return (await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, string accept = "application/json")
    {
        var refreshed = false;
        var forceRefresh = false;
        var attempt = 0;

        while (true)
        {
            var token = await _tokens.GetAsync(forceRefresh);
            forceRefresh = false;

            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd(accept);
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                response.Dispose();
                refreshed = forceRefresh = true;
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                response.Dispose();
                attempt++;
                await Task.Delay(delay);
                continue;
            }

            await EnsureSuccess(response);
            return response;
        }
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var body = (await response.Content.ReadAsStringAsync()).Trim();
        response.Dispose();

        var (message, code) = ErrorInformation(body);
        var detail = $"HTTP {status}" + (body.Length == 0 ? "" : ": " + body);
        var summary = message is null ? null : code is null ? message : $"{message} (Fortnox code {code})";

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => FortnoxException.Auth(
                "Fortnox rejected the access token, even after getting a new one.", detail,
                "Re-authorize: fortnox accounts add <name> --client-id <id> --force"),

            // Fortnox answers 403 for a scope the integration was not granted, and for a module
            // (e.g. bookkeeping) the company does not subscribe to.
            HttpStatusCode.Forbidden => FortnoxException.Auth(
                summary ?? "This account is not allowed to do that.", detail,
                "Check the integration's scopes and the company's licenses; re-authorize with --scopes if a scope is missing."),

            HttpStatusCode.NotFound => FortnoxException.NotFound(summary ?? "The resource does not exist.", detail),

            HttpStatusCode.TooManyRequests => new FortnoxException(
                ErrorCode.RateLimited, "Rate limited by the Fortnox API (25 requests per 5 seconds).", detail,
                "Back off before retrying."),

            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                new FortnoxException(ErrorCode.InvalidInput, summary ?? "The API refused the request.", detail),

            _ when status >= 500 => new FortnoxException(
                ErrorCode.Network, "The Fortnox API returned a server error.", detail,
                "Retry; if it persists Fortnox is having trouble."),

            _ => new FortnoxException(ErrorCode.Error, summary ?? "The request failed.", detail)
        };
    }

    /// <summary>
    /// Fortnox errors look like <c>{"ErrorInformation":{"error":1,"message":"...","code":2000359}}</c>,
    /// with the inner names in either case depending on the endpoint.
    /// </summary>
    private static (string? Message, string? Code) ErrorInformation(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            var info = root.EnumerateObject()
                .FirstOrDefault(p => p.Name.Equals("ErrorInformation", StringComparison.OrdinalIgnoreCase)).Value;
            if (info.ValueKind != JsonValueKind.Object) return (null, null);

            string? Field(string name) => info.EnumerateObject()
                .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())
                .FirstOrDefault();

            return (Field("message"), Field("code"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
