using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fortnox.Console.Infrastructure;

namespace Fortnox.Console.Auth;

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string Scope);

/// <summary>
/// The four calls Fortnox's OAuth server answers: authorization-code exchange, refresh,
/// client credentials and revoke. All of them authenticate with HTTP Basic client_id:client_secret.
/// </summary>
public static class FortnoxOAuth
{
    private const string AuthorizeUrl = "https://apps.fortnox.se/oauth-v1/auth";
    private const string TokenUrl = "https://apps.fortnox.se/oauth-v1/token";
    private const string RevokeUrl = "https://apps.fortnox.se/oauth-v1/revoke";

    private static readonly HttpClient Http = new();

    public static string BuildAuthorizeUrl(
        string clientId, string redirectUri, string scopes, string state, string codeChallenge, bool service)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scopes,
            ["state"] = state,
            ["access_type"] = "offline",
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        if (service) query["account_type"] = "service";

        return AuthorizeUrl + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public static Task<TokenResponse> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string redirectUri, string codeVerifier) =>
        TokenAsync(clientId, clientSecret, null, new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        });

    public static Task<TokenResponse> RefreshAsync(string clientId, string clientSecret, string refreshToken) =>
        TokenAsync(clientId, clientSecret, null, new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

    /// <summary>Without a scope Fortnox issues the scopes from the company's consent.</summary>
    public static Task<TokenResponse> ClientCredentialsAsync(string clientId, string clientSecret, string tenantId) =>
        TokenAsync(clientId, clientSecret, tenantId, new() { ["grant_type"] = "client_credentials" });

    public static async Task<bool> RevokeAsync(string clientId, string clientSecret, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RevokeUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token_type_hint"] = "refresh_token",
                ["token"] = refreshToken
            })
        };
        request.Headers.Authorization = Basic(clientId, clientSecret);

        using var response = await Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static async Task<TokenResponse> TokenAsync(
        string clientId, string clientSecret, string? tenantId, Dictionary<string, string> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = Basic(clientId, clientSecret);
        if (!string.IsNullOrWhiteSpace(tenantId)) request.Headers.Add("TenantId", tenantId);

        using var response = await Http.SendAsync(request);
        var body = (await response.Content.ReadAsStringAsync()).Trim();

        if (!response.IsSuccessStatusCode)
            throw Failure(form["grant_type"], (int)response.StatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new TokenResponse(
            Str(root, "access_token"),
            Str(root, "refresh_token"),
            root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600,
            Str(root, "scope"));
    }

    private static FortnoxException Failure(string grant, int status, string body)
    {
        var detail = $"HTTP {status}" + (body.Length == 0 ? "" : ": " + body);

        if (status >= 500)
            return new FortnoxException(ErrorCode.Network, "The Fortnox OAuth server returned an error.", detail, "Retry once, then stop.");

        // invalid_grant on refresh means the refresh token was used already (they rotate), has
        // passed its 45 days, or the consent was revoked. Only a new consent fixes any of those.
        return grant switch
        {
            "refresh_token" => FortnoxException.Auth(
                "The stored refresh token was rejected - it has expired, was revoked, or was already used.",
                detail,
                "Re-authorize: fortnox accounts add <name> --client-id <id> --force"),
            "client_credentials" => FortnoxException.Auth(
                "Fortnox refused a client-credentials token for this tenant.",
                detail + "\n\nClient credentials only work once the company has activated the integration as a service account.",
                "Re-authorize as a service account: fortnox accounts add <name> --client-id <id> --service --force"),
            _ => FortnoxException.Auth(
                "Fortnox refused the authorization code.",
                detail + "\n\nCodes are single-use and expire after 10 minutes, and the redirect URI must match the one registered for the integration exactly.",
                "Run the add command again.")
        };
    }

    private static AuthenticationHeaderValue Basic(string clientId, string clientSecret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
