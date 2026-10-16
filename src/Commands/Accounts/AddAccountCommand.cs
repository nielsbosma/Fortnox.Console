using System.ComponentModel;
using Fortnox.Console.Auth;
using Fortnox.Console.Infrastructure;
using Fortnox.Console.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Accounts;

public sealed class AddAccountCommand : AsyncCommand<AddAccountCommand.Settings>
{
    public const string DefaultRedirectUri = "http://localhost:8765/callback";

    public sealed class Settings : LocalSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Short name for this account (one Fortnox company), used as --account elsewhere")]
        public string Name { get; init; } = "";

        [CommandOption("--client-id <ID>")]
        [Description("Client id of the integration, from the Fortnox developer portal")]
        public string? ClientId { get; init; }

        [CommandOption("--client-secret <SECRET>")]
        [Description("Client secret of the integration (prompted for, without echo, if omitted)")]
        public string? ClientSecret { get; init; }

        [CommandOption("--tenant-id <ID>")]
        [Description("Tenant id (DatabaseNumber) of a company that already activated the integration as a service account; skips the browser")]
        public string? TenantId { get; init; }

        [CommandOption("--service")]
        [Description("Authorize as a service account, then use client credentials instead of rotating refresh tokens")]
        public bool Service { get; init; }

        [CommandOption("--scopes <SCOPES>")]
        [Description("Space-separated scopes to request (default: everything the built-in commands use)")]
        public string? Scopes { get; init; }

        [CommandOption("--redirect-uri <URI>")]
        [Description("Loopback redirect URI registered on the integration (default: " + DefaultRedirectUri + ")")]
        public string? RedirectUri { get; init; }

        [CommandOption("--no-browser")]
        [Description("Print the authorization URL instead of opening a browser")]
        public bool NoBrowser { get; init; }

        [CommandOption("--timeout <MINUTES>")]
        [Description("How long to wait for the browser redirect (default 5)")]
        public int Timeout { get; init; } = 5;

        [CommandOption("--force")]
        [Description("Replace the credentials of an account that already exists")]
        public bool Force { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) return ValidationResult.Error("An account name is required.");
            if (string.IsNullOrWhiteSpace(ClientId)) return ValidationResult.Error("--client-id is required.");
            return base.Validate();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        var name = settings.Name.Trim();
        var clientId = settings.ClientId!.Trim();
        var store = SecretStoreFactory.Create();
        var config = ConfigStore.Load();

        var existing = config.Accounts.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !settings.Force)
            throw FortnoxException.Invalid(
                $"An account named '{existing}' already exists.",
                $"Pick a different name, or re-authorize it: fortnox accounts add {existing} --client-id {clientId} --force");

        name = existing ?? name;

        var secret = settings.ClientSecret?.Trim();
        if (string.IsNullOrWhiteSpace(secret)) secret = Prompt(name);
        if (string.IsNullOrWhiteSpace(secret)) throw FortnoxException.Invalid("A client secret is required.");

        var scopes = string.IsNullOrWhiteSpace(settings.Scopes) ? Resources.DefaultScopes : settings.Scopes.Trim();
        var secrets = new AccountSecrets { ClientSecret = secret };
        string tenantId;
        string mode;

        if (!string.IsNullOrWhiteSpace(settings.TenantId))
        {
            // The company has consented already; client credentials need nothing else.
            tenantId = settings.TenantId.Trim();
            mode = AuthModes.ClientCredentials;
            TokenProvider.Apply(secrets, await FortnoxOAuth.ClientCredentialsAsync(clientId, secret, tenantId));
        }
        else
        {
            if (System.Console.IsInputRedirected && !settings.NoBrowser)
                System.Console.Error.WriteLine("No terminal attached; printing the URL rather than relying on a browser.");

            var redirectUri = settings.RedirectUri ?? DefaultRedirectUri;
            var login = await BrowserLogin.RunAsync(clientId, redirectUri, scopes, settings.Service,
                openBrowser: !settings.NoBrowser, TimeSpan.FromMinutes(Math.Max(1, settings.Timeout)));

            TokenProvider.Apply(secrets,
                await FortnoxOAuth.ExchangeCodeAsync(clientId, secret, login.Code, redirectUri, login.CodeVerifier));

            tenantId = Jwt.Claim(secrets.AccessToken, "tenantId") ?? "";
            mode = settings.Service && tenantId.Length > 0 ? AuthModes.ClientCredentials : AuthModes.RefreshToken;
        }

        // Verify before storing: credentials that do not work are worse than no account at all,
        // because every later failure looks like a problem with the command being run.
        var client = new FortnoxClient(new FixedToken(secrets.AccessToken));
        using var company = await client.GetAsync("companyinformation");
        var identity = FortnoxIdentity.Describe(company);
        var organization = FortnoxIdentity.Organization(company);
        if (tenantId.Length == 0) tenantId = FortnoxIdentity.TenantId(company);

        using (var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None))
        {
            // Secrets go in first: a config entry with no secrets is a broken account, while an
            // orphaned secret is invisible and harmless.
            secrets.Save(store, name);

            config = ConfigStore.Load();
            config.Accounts[name] = new AccountConfig
            {
                ClientId = clientId,
                TenantId = tenantId,
                AuthMode = mode,
                Scopes = scopes,
                Identity = identity,
                Organization = organization,
                AddedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            ConfigStore.Save(config);
        }

        var result = new Dictionary<string, object?>
        {
            ["status"] = existing is null ? "added" : "replaced",
            ["name"] = name,
            ["identity"] = identity,
            ["organization"] = organization,
            ["tenantId"] = tenantId,
            ["authMode"] = mode,
            ["secretStore"] = store.Name,
            ["configDir"] = ConfigStore.ConfigDir,
            ["nextStep"] = $"fortnox customers list --account {name}"
        };

        if (mode == AuthModes.RefreshToken)
            result["note"] = "The refresh token expires 45 days after its last use. Use the account at least that often, or re-run this command.";

        Output.WriteObject(result);
        return 0;
    }

    /// <summary>Prompts on stderr, so stdout stays a clean payload even when the secret is typed in.</summary>
    private static string Prompt(string name)
    {
        if (System.Console.IsInputRedirected)
            throw FortnoxException.Invalid(
                "No client secret given and no terminal to prompt on.",
                $"fortnox accounts add {name} --client-id <id> --client-secret <secret>");

        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(System.Console.Error) });

        return console.Prompt(new TextPrompt<string>($"Client secret for [green]{name}[/]:")
            .Secret()
            .Validate(value => string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Error("Cannot be empty")
                : ValidationResult.Success()));
    }
}
