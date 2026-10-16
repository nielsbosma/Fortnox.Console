using System.ComponentModel;
using Fortnox.Console.Auth;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Accounts;

public sealed class TestAccountCommand : AsyncCommand<TestAccountCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Account name")]
        public string Name { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        var account = AccountResolver.Resolve(settings.Name);
        var client = new FortnoxClient(new TokenProvider(account.Name, account.Config));

        using var company = await client.GetAsync("companyinformation");
        var identity = FortnoxIdentity.Describe(company);
        var organization = FortnoxIdentity.Organization(company);

        var result = new Dictionary<string, object?>
        {
            ["name"] = account.Name,
            ["identity"] = identity,
            ["organization"] = organization,
            ["tenantId"] = account.Config.TenantId,
            ["authMode"] = account.Config.AuthMode,
            ["status"] = "valid",
            ["company"] = Json.ToObject(company.RootElement)
        };

        // Drift means the account was re-authorized against a different company - worth saying
        // out loud, because every command run against this account now changes other books.
        var drifted = Drift(account.Organization, organization) ?? Drift(account.Identity, identity);
        if (drifted is not null) result["warning"] = drifted;

        Output.WriteObject(result);
        return 0;
    }

    private static string? Drift(string recorded, string current) =>
        !string.IsNullOrWhiteSpace(recorded) &&
        !string.IsNullOrWhiteSpace(current) &&
        !current.Equals(recorded, StringComparison.OrdinalIgnoreCase)
            ? $"This account was added as {recorded}, but its credentials now report {current}."
            : null;
}
