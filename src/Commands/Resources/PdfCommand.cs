using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

/// <summary>
/// Downloads the printed document. <c>preview</c> renders without marking the document as
/// printed/sent; <c>print</c> does mark it.
/// </summary>
public sealed class PdfCommand : AsyncCommand<PdfCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("The document number")]
        public string Id { get; init; } = "";

        [CommandOption("-o|--output <FILE>")]
        [Description("Where to write the PDF (default: <resource>-<id>.pdf in the current directory)")]
        public string? OutputPath { get; init; }

        [CommandOption("--mark-sent")]
        [Description("Use 'print' instead of 'preview', which marks the document as sent in Fortnox")]
        public bool MarkSent { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        var client = settings.CreateClient();

        var segment = settings.MarkSent ? "print" : "preview";
        var (bytes, contentType) = await client.GetBytesAsync($"{resource.Path}/{ResourcePath.Id(settings.Id)}/{segment}");

        var file = Path.GetFullPath(settings.OutputPath ?? $"{resource.Singular.ToLowerInvariant()}-{settings.Id.Replace('/', '-')}.pdf");
        await File.WriteAllBytesAsync(file, bytes);

        Output.WriteObject(new Dictionary<string, object?>
        {
            ["status"] = "saved",
            ["file"] = file,
            ["bytes"] = bytes.Length,
            ["contentType"] = contentType,
            ["markedSent"] = settings.MarkSent
        });
        return 0;
    }
}
