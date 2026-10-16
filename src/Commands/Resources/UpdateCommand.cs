using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

/// <summary>
/// PUT with only the fields given. Fortnox merges top-level fields, but replaces row arrays
/// (InvoiceRows, VoucherRows, ...) wholesale - sending one row deletes the others.
/// </summary>
public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The resource id")]
        public string Id { get; init; } = "";

        [CommandOption("--data <JSON>")]
        [Description("Fields to change as JSON, wrapped or not: inline, @file.json, or - for stdin")]
        public string? Data { get; init; }

        [CommandOption("--set <FIELD=VALUE>")]
        [Description("Set one field, repeatable: Field=value for a string, Field:=json for numbers, booleans and arrays")]
        public string[]? Set { get; init; }

        [CommandOption("--query <KEY=VALUE>")]
        [Description("Extra query parameter, repeatable")]
        public string[]? Query { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        var body = Body.Build(settings.Data, settings.Set, resource.Singular);
        var client = settings.CreateClient();

        var path = ResourcePath.WithQuery($"{resource.Path}/{ResourcePath.Id(settings.Id)}", ResourcePath.Pairs(settings.Query));
        using var doc = await client.PutAsync(path, body);
        Output.Write(doc);
        return 0;
    }
}
