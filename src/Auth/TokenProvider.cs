using Fortnox.Console.Infrastructure;
using Fortnox.Console.Storage;

namespace Fortnox.Console.Auth;

public static class AuthModes
{
    /// <summary>Service account: a fresh token on demand from client id + secret + tenant id.</summary>
    public const string ClientCredentials = "client_credentials";

    /// <summary>User consent: a rotating refresh token, valid 45 days from its last use.</summary>
    public const string RefreshToken = "refresh_token";
}

public interface ITokenSource
{
    Task<string> GetAsync(bool forceRefresh = false);
}

/// <summary>A token already in hand - used to verify a new account before anything is stored.</summary>
public sealed class FixedToken(string token) : ITokenSource
{
    public Task<string> GetAsync(bool forceRefresh = false) => Task.FromResult(token);
}

/// <summary>
/// Hands out a valid access token for one account, getting a new one when it is about to expire.
///
/// Fortnox rotates refresh tokens: using one invalidates it. Two processes refreshing from the
/// same stored token would leave the loser holding a dead one, and the account broken until it is
/// re-authorized. So the read-refresh-write cycle happens under a cross-process lock, and the
/// cached token is re-read inside it - whoever waited finds the token the winner just stored.
/// </summary>
public sealed class TokenProvider(string accountName, AccountConfig config) : ITokenSource
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    private string? _token;

    public async Task<string> GetAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _token is not null) return _token;

        var store = SecretStoreFactory.Create();

        using (await FileLock.AcquireAsync(ConfigStore.TokenLockPath, CancellationToken.None))
        {
            var secrets = AccountSecrets.Load(store, accountName)
                ?? throw FortnoxException.Auth($"Account '{accountName}' has no stored credentials.", null,
                    $"fortnox accounts add {accountName} --client-id <id> --force");

            var stored = !string.IsNullOrEmpty(secrets.AccessToken) &&
                         secrets.AccessTokenExpiresAt - Skew > DateTimeOffset.UtcNow;

            // On a forced refresh (the API answered 401) a stored token other than the one that
            // just failed was put there by another process meanwhile, and is worth trying first.
            if (stored && (!forceRefresh || secrets.AccessToken != _token))
                return _token = secrets.AccessToken;

            Apply(secrets, await Acquire(secrets));
            secrets.Save(store, accountName);
            return _token = secrets.AccessToken;
        }
    }

    private Task<TokenResponse> Acquire(AccountSecrets secrets)
    {
        if (config.AuthMode == AuthModes.ClientCredentials)
        {
            if (string.IsNullOrWhiteSpace(config.TenantId))
                throw FortnoxException.Auth($"Account '{accountName}' uses client credentials but has no tenant id.", null,
                    $"fortnox accounts add {accountName} --client-id {config.ClientId} --tenant-id <id> --force");

            return FortnoxOAuth.ClientCredentialsAsync(config.ClientId, secrets.ClientSecret, config.TenantId);
        }

        if (string.IsNullOrWhiteSpace(secrets.RefreshToken))
            throw FortnoxException.Auth($"Account '{accountName}' has no refresh token.", null,
                $"fortnox accounts add {accountName} --client-id {config.ClientId} --force");

        return FortnoxOAuth.RefreshAsync(config.ClientId, secrets.ClientSecret, secrets.RefreshToken);
    }

    /// <summary>Copies a token response into the secrets, keeping the old refresh token if none came back.</summary>
    public static void Apply(AccountSecrets secrets, TokenResponse response)
    {
        secrets.AccessToken = response.AccessToken;
        secrets.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn);
        if (!string.IsNullOrEmpty(response.RefreshToken)) secrets.RefreshToken = response.RefreshToken;
    }
}
