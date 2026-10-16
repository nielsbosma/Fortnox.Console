using System.Text.Json;

namespace Fortnox.Console.Storage;

/// <summary>
/// Everything secret about one account, stored as one entry so a refresh replaces the rotated
/// refresh token and the new access token in a single write.
/// </summary>
public sealed class AccountSecrets
{
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Rotated by Fortnox on every use - the previous one stops working the moment a new one is
    /// issued. Unused by client_credentials accounts.
    /// </summary>
    public string RefreshToken { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    public static AccountSecrets? Load(ISecretStore store, string name)
    {
        var raw = store.Get(SecretKeys.Account(name));
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<AccountSecrets>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(ISecretStore store, string name) =>
        store.Set(SecretKeys.Account(name), JsonSerializer.Serialize(this));
}
