using System.ComponentModel;
using Fortnox.Console.Auth;
using Fortnox.Console.Infrastructure;
using Fortnox.Console.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Accounts;

public sealed class RemoveAccountCommand : AsyncCommand<RemoveAccountCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Account name")]
        public string Name { get; init; } = "";

        [CommandOption("--yes")]
        [Description("Skip the confirmation prompt")]
        public bool Yes { get; init; }

        [CommandOption("--revoke")]
        [Description("Also revoke the refresh token at Fortnox, so it stops working everywhere")]
        public bool Revoke { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        var store = SecretStoreFactory.Create();
        var config = ConfigStore.Load();

        var name = config.Accounts.Keys.FirstOrDefault(k => k.Equals(settings.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FortnoxException(
                ErrorCode.NoAccount,
                $"No account named '{settings.Name}'.",
                null,
                "fortnox accounts list");

        var account = config.Accounts[name];

        if (!settings.Yes)
        {
            if (System.Console.IsInputRedirected)
                throw FortnoxException.Invalid(
                    $"Removing '{name}' needs confirmation and there is no terminal to ask on.",
                    $"fortnox accounts remove {name} --yes");

            var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(System.Console.Error) });
            var label = string.IsNullOrWhiteSpace(account.Identity) ? name : $"{name} ({account.Identity})";
            if (!console.Confirm($"Remove account [green]{label}[/]?", false))
                throw FortnoxException.Invalid("Cancelled.");
        }

        // Revoke outside the lock - it is a network call, and FileLock is held only for writes.
        bool? revoked = null;
        var secrets = AccountSecrets.Load(store, name);
        if (settings.Revoke && secrets is { RefreshToken.Length: > 0 })
            revoked = await FortnoxOAuth.RevokeAsync(account.ClientId, secrets.ClientSecret, secrets.RefreshToken);

        using (var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None))
        {
            store.Delete(SecretKeys.Account(name));

            config = ConfigStore.Load();
            config.Accounts.Remove(name);
            ConfigStore.Save(config);
        }

        var result = new Dictionary<string, object?>
        {
            ["status"] = "removed",
            ["name"] = name,
            ["identity"] = account.Identity
        };

        // The credentials are gone from this machine, which is not the same as the company
        // withdrawing its consent. Say which one happened.
        result["note"] = revoked switch
        {
            true => "Credentials deleted locally and the refresh token revoked at Fortnox.",
            false => "Credentials deleted locally, but Fortnox did not confirm the revoke. Remove the integration under Integrations in Fortnox if it must stop working.",
            null when account.AuthMode == AuthModes.ClientCredentials =>
                "Credentials deleted locally. The company's service-account consent still exists; remove the integration in Fortnox to withdraw it.",
            null => "Credentials deleted locally. The refresh token was not revoked; pass --revoke, or remove the integration in Fortnox."
        };

        Output.WriteObject(result);
        return 0;
    }
}
