using System.ComponentModel;
using Fortnox.Console.Auth;
using Spectre.Console.Cli;

namespace Fortnox.Console.Infrastructure;

/// <summary>Settings for every command, including the ones that never call the API.</summary>
public class LocalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Print raw JSON instead of YAML, for scripting")]
    public bool Json { get; init; }

    /// <summary>
    /// Commands render through <see cref="Output"/> without knowing their settings, so the
    /// choice is recorded once, here, before anything is written.
    /// </summary>
    public void ApplyOutputMode() => Output.UseJson = Json;
}

/// <summary>Settings for a command that talks to the Fortnox API as one configured account.</summary>
public class ApiSettings : LocalSettings
{
    /// <summary>
    /// Required, but not declared <c>required</c> - a missing value is resolved by
    /// <see cref="AccountResolver"/> so the failure carries the list of configured accounts
    /// instead of Spectre's bare parse error.
    /// </summary>
    [CommandOption("-a|--account <ACCOUNT>")]
    [Description("Account to run against (required; see 'fortnox accounts list')")]
    public string? Account { get; init; }

    public FortnoxClient CreateClient()
    {
        ApplyOutputMode();

        var account = AccountResolver.Resolve(Account);
        return new FortnoxClient(new TokenProvider(account.Name, account.Config));
    }
}
