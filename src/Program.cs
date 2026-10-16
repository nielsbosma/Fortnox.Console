using Fortnox.Console.Commands;
using Fortnox.Console.Commands.Accounts;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

// The error envelope is rendered before any command has run, so the format has to be known
// before Spectre parses anything. Commands overwrite this with the same value.
Output.UseJson = args.Contains("--json");

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("fortnox");
    config.SetExceptionHandler((ex, _) => ErrorOutput.Write(ex));

    config.AddCommand<CompanyCommand>("company")
        .WithDescription("Get the company information for an account");

    config.AddBranch("accounts", accounts =>
    {
        accounts.SetDescription("Manage Fortnox accounts (one per company) and their credentials");
        accounts.AddCommand<AddAccountCommand>("add")
            .WithDescription("Authorize a Fortnox company and store its credentials in the OS keystore");
        accounts.AddCommand<ListAccountsCommand>("list")
            .WithDescription("List configured accounts");
        accounts.AddCommand<TestAccountCommand>("test")
            .WithDescription("Check that an account's stored credentials still work");
        accounts.AddCommand<RemoveAccountCommand>("remove")
            .WithDescription("Remove an account and delete its stored credentials");
    });
});

return app.Run(args);
