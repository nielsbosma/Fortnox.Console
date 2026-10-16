using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

public sealed class GetCommand : AsyncCommand<GetCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The resource id (e.g. a CustomerNumber, DocumentNumber, or SERIES/NUMBER for vouchers)")]
        public string Id { get; init; } = "";

        [CommandOption("--query <KEY=VALUE>")]
        [Description("Extra query parameter, repeatable, e.g. --query financialyear=3")]
        public string[]? Query { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        var client = settings.CreateClient();

        var path = ResourcePath.WithQuery($"{resource.Path}/{ResourcePath.Id(settings.Id)}", ResourcePath.Pairs(settings.Query));
        using var doc = await client.GetAsync(path);
        Output.Write(doc);
        return 0;
    }
}
