using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

/// <summary>
/// A verb on one resource - <c>invoices bookkeep 123</c> is <c>PUT invoices/123/bookkeep</c>.
/// The irreversible ones (bookkeeping, cancelling, crediting, emailing a customer) need --yes.
/// </summary>
public sealed class ActionCommand : AsyncCommand<ActionCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The resource id")]
        public string Id { get; init; } = "";

        [CommandOption("--yes")]
        [Description("Confirm an action that cannot be undone")]
        public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var (resource, action) = context.Data as ActionData
            ?? throw new InvalidOperationException("Command registered without its action.");

        if (action.Irreversible)
            ResourcePath.RequireYes(settings.Yes, $"'{resource.Command} {action.Name}' on {resource.Singular} {settings.Id}");

        var client = settings.CreateClient();
        var path = $"{resource.Path}/{ResourcePath.Id(settings.Id)}/{action.Segment}";

        using var doc = await client.SendJsonAsync(new HttpMethod(action.Method), path, null);
        Output.Write(doc);
        return 0;
    }
}
