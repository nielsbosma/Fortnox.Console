using System.ComponentModel;
using System.Text.Json;
using Fortnox.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands.Resources;

public sealed class ListCommand : AsyncCommand<ListCommand.Settings>
{
    public const int MaxLimit = 500;

    public sealed class Settings : ApiSettings
    {
        [CommandOption("--filter <FILTER>")]
        [Description("Fortnox list filter, e.g. unpaid, unbooked, active (see agent-readme for each resource's filters)")]
        public string? Filter { get; init; }

        [CommandOption("--from-date <DATE>")]
        [Description("Only items on or after this date (YYYY-MM-DD)")]
        public string? FromDate { get; init; }

        [CommandOption("--to-date <DATE>")]
        [Description("Only items on or before this date (YYYY-MM-DD)")]
        public string? ToDate { get; init; }

        [CommandOption("--last-modified <TIMESTAMP>")]
        [Description("Only items changed since this time (YYYY-MM-DD HH:MM)")]
        public string? LastModified { get; init; }

        [CommandOption("--sort-by <FIELD>")]
        [Description("Field to sort by, e.g. documentnumber, customername")]
        public string? SortBy { get; init; }

        [CommandOption("--sort-order <ORDER>")]
        [Description("ascending or descending")]
        public string? SortOrder { get; init; }

        [CommandOption("--query <KEY=VALUE>")]
        [Description("Extra query parameter, repeatable, e.g. --query customernumber=1001 --query financialyear=3")]
        public string[]? Query { get; init; }

        [CommandOption("--page <N>")]
        [Description("Page number (default 1)")]
        public int? Page { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Items per page, max 500 (default 100)")]
        public int Limit { get; init; } = 100;

        [CommandOption("--all")]
        [Description("Fetch every page and return one combined list")]
        public bool All { get; init; }

        [CommandOption("--max-pages <N>")]
        [Description("Stop --all after this many pages (default 50)")]
        public int MaxPages { get; init; } = 50;

        public override ValidationResult Validate()
        {
            if (Limit is < 1 or > MaxLimit) return ValidationResult.Error($"--limit must be between 1 and {MaxLimit}.");
            if (All && Page is not null) return ValidationResult.Error("--all and --page cannot be combined.");
            return base.Validate();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resource = ResourceCommand.ResourceOf(context);
        var client = settings.CreateClient();

        var query = new List<KeyValuePair<string, string?>>
        {
            new("filter", settings.Filter),
            new("fromdate", settings.FromDate),
            new("todate", settings.ToDate),
            new("lastmodified", settings.LastModified),
            new("sortby", settings.SortBy),
            new("sortorder", settings.SortOrder),
            new("limit", settings.Limit.ToString())
        };
        query.AddRange(ResourcePath.Pairs(settings.Query));

        if (!settings.All)
        {
            var path = ResourcePath.WithQuery(resource.Path, query.Append(new("page", settings.Page?.ToString())));
            using var doc = await client.GetAsync(path);
            Output.Write(doc);
            return 0;
        }

        var items = new List<object?>();
        int page = 1, totalPages = 1;
        long totalResources = 0;

        do
        {
            var path = ResourcePath.WithQuery(resource.Path, query.Append(new("page", page.ToString())));
            using var doc = await client.GetAsync(path);
            var root = doc.RootElement;

            if (root.TryGetProperty(resource.Plural, out var list) && list.ValueKind == JsonValueKind.Array)
                items.AddRange(list.EnumerateArray().Select(Json.ToObject));

            if (root.TryGetProperty("MetaInformation", out var meta))
            {
                if (meta.TryGetProperty("@TotalPages", out var tp) && tp.TryGetInt32(out var t)) totalPages = t;
                if (meta.TryGetProperty("@TotalResources", out var tr) && tr.TryGetInt64(out var r)) totalResources = r;
            }
            page++;
        } while (page <= totalPages && page <= settings.MaxPages);

        var result = new Dictionary<string, object?>
        {
            [resource.Plural] = items,
            ["MetaInformation"] = new Dictionary<string, object?>
            {
                ["@TotalResources"] = totalResources,
                ["@TotalPages"] = totalPages,
                ["@PagesFetched"] = page - 1
            }
        };

        if (page <= totalPages)
            result["warning"] = $"Stopped after {settings.MaxPages} pages of {totalPages}; raise --max-pages or narrow the query.";

        Output.WriteObject(result);
        return 0;
    }
}
