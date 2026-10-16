using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

public sealed class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--data <JSON>")]
        [Description("The resource as JSON, wrapped or not: inline, @file.json, or - for stdin")]
        public string? Data { get; init; }

        [CommandOption("--set <FIELD=VALUE>")]
        [Description("Set one field, repeatable: Field=value for a string, Field:=json for numbers, booleans and arrays")]
        public string[]? Set { get; init; }

        [CommandOption("--query <KEY=VALUE>")]
        [Description("Extra query parameter, repeatable, e.g. --query financialyear=3")]
        public string[]? Query { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        var body = Body.Build(settings.Data, settings.Set, resource.Singular);
        var client = settings.CreateClient();

        using var doc = await client.PostAsync(ResourcePath.WithQuery(resource.Path, ResourcePath.Pairs(settings.Query)), body);
        Output.Write(doc);
        return 0;
    }
}
