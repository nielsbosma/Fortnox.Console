using System.Text;
using System.Text.Json;

namespace Fortnox.Console.Auth;

/// <summary>
/// Reads claims out of a Fortnox access token. The signature is not verified - Fortnox does not
/// publish the key, and the token came straight from its own token endpoint.
/// </summary>
public static class Jwt
{
    public static string? Claim(string token, string name)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (!doc.RootElement.TryGetProperty(name, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
