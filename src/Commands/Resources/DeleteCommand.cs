using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

public sealed class DeleteCommand : AsyncCommand<DeleteCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The resource id")]
        public string Id { get; init; } = "";

        [CommandOption("--yes")]
        [Description("Confirm the delete; it cannot be undone")]
        public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        ResourcePath.RequireYes(settings.Yes, $"Deleting {resource.Singular} {settings.Id}");

        var client = settings.CreateClient();
        using var _ = await client.DeleteAsync($"{resource.Path}/{ResourcePath.Id(settings.Id)}");

        Output.WriteObject(new Dictionary<string, object?>
        {
            ["status"] = "deleted",
            ["resource"] = resource.Singular,
            ["id"] = settings.Id
        });
        return 0;
    }
}
