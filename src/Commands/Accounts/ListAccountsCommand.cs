using System.ComponentModel;
using Fortnox.Console.Auth;
using Fortnox.Console.Infrastructure;
using Fortnox.Console.Storage;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Accounts;

public sealed class ListAccountsCommand : AsyncCommand<ListAccountsCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandOption("--check")]
        [Description("Call the API once per account instead of reporting stored state")]
        public bool Check { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        var store = SecretStoreFactory.Create();
        var config = ConfigStore.Load();
        var accounts = new List<object>();

        foreach (var (name, account) in config.Accounts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            accounts.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["identity"] = account.Identity,
                ["organization"] = account.Organization,
                ["tenantId"] = account.TenantId,
                ["authMode"] = account.AuthMode,
                ["addedAt"] = account.AddedAt,
                ["status"] = await StatusOf(name, account, store, settings.Check)
            });
        }

        Output.WriteObject(new Dictionary<string, object?>
        {
            ["accounts"] = accounts,
            ["count"] = accounts.Count,
            ["secretStore"] = store.Name,
            ["configDir"] = ConfigStore.ConfigDir
        });

        return 0;
    }

    private static async Task<string> StatusOf(string name, AccountConfig account, ISecretStore store, bool check)
    {
        if (AccountSecrets.Load(store, name) is null) return "missing_credentials";

        // Without --check this is what is actually known locally: credentials are stored, and
        // whether they still work is a question only Fortnox can answer.
        if (!check) return "stored";

        try
        {
            var client = new FortnoxClient(new TokenProvider(name, account));
            using var _ = await client.GetAsync("companyinformation");
            return "valid";
        }
        catch (FortnoxException ex) when (ex.Code == ErrorCode.AuthRequired)
        {
            return "rejected";
        }
        catch (Exception)
        {
            return "unreachable";
        }
    }
}
