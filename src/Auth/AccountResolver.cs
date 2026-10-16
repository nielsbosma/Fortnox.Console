using Fortnox.Console.Infrastructure;
using Fortnox.Console.Storage;

namespace Fortnox.Console.Auth;

public sealed record ResolvedAccount(string Name, AccountConfig Config)
{
    public string Identity => Config.Identity;
    public string Organization => Config.Organization;
}

/// <summary>
/// The account is always explicit - no stored default, no environment variable, no implicit
/// fallback when only one account is configured.
///
/// The failure this prevents is an agent working from a summarized transcript booking an invoice
/// in the wrong company: the call succeeds, and nothing in the output says it went to the
/// account you did not mean.
/// </summary>
public static class AccountResolver
{
    public static ResolvedAccount Resolve(string? requested)
    {
        var config = ConfigStore.Load();

        if (string.IsNullOrWhiteSpace(requested))
            throw new FortnoxException(
                ErrorCode.NoAccount,
                "No account specified. Pass --account <name>.",
                Describe(config),
                "fortnox accounts list");

        var name = config.Accounts.Keys.FirstOrDefault(k => k.Equals(requested, StringComparison.OrdinalIgnoreCase));

        if (name is null || !config.Accounts.TryGetValue(name, out var account))
            throw new FortnoxException(
                ErrorCode.NoAccount,
                $"No account named '{requested}'.",
                Describe(config),
                "fortnox accounts list");

        // A config entry with no secrets is an account that was half-removed, or one whose
        // keystore entry was cleared behind our back. Either way re-adding is the fix.
        if (AccountSecrets.Load(SecretStoreFactory.Create(), name) is null)
            throw new FortnoxException(
                ErrorCode.AuthRequired,
                $"Account '{name}' has no stored credentials.",
                "The config entry exists but the keystore has nothing under it.",
                $"fortnox accounts add {name} --client-id <id> --force");

        return new ResolvedAccount(name, account);
    }

    private static string Describe(FortnoxConfig config)
    {
        if (config.Accounts.Count == 0)
            return "No accounts are configured yet. Run 'fortnox accounts add <name> --client-id <id>'.";

        var listed = config.Accounts
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => string.IsNullOrWhiteSpace(kv.Value.Identity) ? kv.Key : $"{kv.Key} ({kv.Value.Identity})");

        return "Configured accounts: " + string.Join(", ", listed);
    }
}
