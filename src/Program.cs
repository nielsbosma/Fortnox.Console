using Fortnox.Console.Commands;
using Fortnox.Console.Commands.Accounts;
using Fortnox.Console.Commands.Resources;
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

    config.AddCommand<AgentReadmeCommand>("agent-readme")
        .WithDescription("Print the operating manual for an LLM agent driving this CLI");

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

    // Every resource gets the same verbs, driven by its definition in Infrastructure/Resource.cs.
    foreach (var resource in ResourceCatalog.All)
    {
        config.AddBranch(resource.Command, branch =>
        {
            branch.SetDescription(resource.Description);
            var one = resource.Singular.ToLowerInvariant();

            if (resource.Ops.HasFlag(Ops.List))
                branch.AddCommand<ListCommand>("list").WithData(resource)
                    .WithDescription($"List {resource.Description.ToLowerInvariant()}");
            if (resource.Ops.HasFlag(Ops.Get))
                branch.AddCommand<GetCommand>("get").WithData(resource)
                    .WithDescription($"Get one {one} by {resource.IdField}");
            if (resource.Ops.HasFlag(Ops.Create))
                branch.AddCommand<CreateCommand>("create").WithData(resource)
                    .WithDescription($"Create a {one}");
            if (resource.Ops.HasFlag(Ops.Update))
                branch.AddCommand<UpdateCommand>("update").WithData(resource)
                    .WithDescription($"Update a {one} (only the fields given)");
            if (resource.Ops.HasFlag(Ops.Delete))
                branch.AddCommand<DeleteCommand>("delete").WithData(resource)
                    .WithDescription($"Delete a {one} (needs --yes)");
            if (resource.Pdf)
                branch.AddCommand<PdfCommand>("pdf").WithData(resource)
                    .WithDescription($"Download a {one} as PDF");

            foreach (var action in resource.AllActions)
                branch.AddCommand<ActionCommand>(action.Name).WithData(new ActionData(resource, action))
                    .WithDescription(action.Description + (action.Irreversible ? " (needs --yes)" : ""));
        });
    }

    config.AddBranch("api", api =>
    {
        api.SetDescription("Call any Fortnox v3 endpoint directly");
        api.AddCommand<ApiCommand>("get").WithData(HttpMethod.Get).WithDescription("GET a path");
        api.AddCommand<ApiCommand>("post").WithData(HttpMethod.Post).WithDescription("POST to a path");
        api.AddCommand<ApiCommand>("put").WithData(HttpMethod.Put).WithDescription("PUT to a path");
        api.AddCommand<ApiCommand>("delete").WithData(HttpMethod.Delete).WithDescription("DELETE a path (needs --yes)");
    });
});

return app.Run(args);
