using System.ComponentModel;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands;

/// <summary>
/// The escape hatch: any Fortnox v3 endpoint the built-in commands do not cover, with the same
/// account handling, token refresh, retries and error envelope. The body is sent exactly as given.
/// </summary>
public sealed class ApiCommand : AsyncCommand<ApiCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Path under https://api.fortnox.se/3/, e.g. 'settings/company' or 'invoices?filter=unpaid'")]
        public string Path { get; init; } = "";

        [CommandOption("--data <JSON>")]
        [Description("Request body: inline JSON, @file.json, or - for stdin (sent as-is, not wrapped)")]
        public string? Data { get; init; }

        [CommandOption("--query <KEY=VALUE>")]
        [Description("Extra query parameter, repeatable")]
        public string[]? Query { get; init; }

        [CommandOption("--yes")]
        [Description("Confirm a DELETE")]
        public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var method = context.Data as HttpMethod ?? HttpMethod.Get;

        if (method == HttpMethod.Delete)
            ResourcePath.RequireYes(settings.Yes, $"DELETE {settings.Path}");

        // Accept a pasted full URL or a leading "/3/" as well as a bare relative path.
        var path = settings.Path.Trim();
        const string prefix = "https://api.fortnox.se/3/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path[prefix.Length..];
        path = path.TrimStart('/');
        if (path.StartsWith("3/")) path = path[2..];

        var client = settings.CreateClient();
        using var doc = await client.SendJsonAsync(method,
            ResourcePath.WithQuery(path, ResourcePath.Pairs(settings.Query)), Body.Raw(settings.Data));
        Output.Write(doc);
        return 0;
    }
}
