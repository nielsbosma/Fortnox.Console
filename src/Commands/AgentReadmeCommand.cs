using System.Text;
using Fortnox.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Fortnox.Console.Commands;

/// <summary>
/// The manual an agent reads before its first call. Markdown by default so it can be pasted into
/// a system prompt or a CLAUDE.md; <c>--json</c> gives the same rules as data. The resource
/// section is generated from <see cref="ResourceCatalog.All"/>, so it cannot fall behind the commands.
/// </summary>
public sealed class AgentReadmeCommand : Command<AgentReadmeCommand.Settings>
{
    public sealed class Settings : LocalSettings;

    public override int Execute(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();

        if (settings.Json)
        {
            Output.WriteObject(Structured());
            return 0;
        }

        System.Console.Out.WriteLine(Readme + "\n" + ResourceSection());
        return 0;
    }

    private static readonly List<string> Rules =
    [
        "Always pass --account. There is no default account and no environment variable.",
        "Run 'fortnox accounts list' first if you do not know which accounts exist; ask the human which company to use.",
        "On code auth_required, stop and surface the remediation string. Do not retry.",
        "Bookkeeping, cancelling, crediting, emailing and deleting need --yes and cannot be undone. Read the resource back and confirm with the human first.",
        "Updates replace row arrays (InvoiceRows, VoucherRows, ...) wholesale. Send every row you want to keep.",
        "Booked invoices cannot be updated; credit them instead.",
        "--set Field=value sends a string; --set Field:=json sends numbers, booleans and arrays.",
        "Use 'fortnox api get|post|put|delete <path>' for endpoints without a built-in command.",
        "Use --json when you are going to parse the output."
    ];

    private static Dictionary<string, object?> Structured() => new()
    {
        ["tool"] = "fortnox",
        ["rules"] = Rules,
        ["resources"] = ResourceCatalog.All.Select(r => new Dictionary<string, object?>
        {
            ["command"] = r.Command,
            ["path"] = r.Path,
            ["id"] = r.IdField,
            ["scope"] = r.Scope,
            ["verbs"] = Verbs(r),
            ["filters"] = r.Filters?.ToList(),
            ["note"] = r.Note
        }).ToList(),
        ["exitCodes"] = new Dictionary<string, object?>
        {
            ["0"] = "ok",
            ["1"] = "error - unclassified, report and stop",
            ["2"] = "network - retry once, then stop",
            ["3"] = "auth_required - stop, surface the remediation to a human",
            ["4"] = "not_found - do not retry",
            ["5"] = "rate_limited - back off before retrying",
            ["6"] = "invalid_input - fix the call",
            ["7"] = "no_account - run fortnox accounts list"
        }
    };

    private static List<string> Verbs(Resource r)
    {
        var verbs = new List<string>();
        if (r.Ops.HasFlag(Ops.List)) verbs.Add("list");
        if (r.Ops.HasFlag(Ops.Get)) verbs.Add("get");
        if (r.Ops.HasFlag(Ops.Create)) verbs.Add("create");
        if (r.Ops.HasFlag(Ops.Update)) verbs.Add("update");
        if (r.Ops.HasFlag(Ops.Delete)) verbs.Add("delete*");
        if (r.Pdf) verbs.Add("pdf");
        verbs.AddRange(r.AllActions.Select(a => a.Irreversible ? a.Name + "*" : a.Name));
        return verbs;
    }

    private static string ResourceSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Resources");
        sb.AppendLine();
        sb.AppendLine("Every resource takes the same verbs. `*` marks a verb that needs `--yes`.");
        sb.AppendLine();
        sb.AppendLine("| Command | Id | Verbs | List filters | Scope |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var r in ResourceCatalog.All)
            sb.AppendLine($"| `{r.Command}` | {r.IdField} | {string.Join(", ", Verbs(r))} | {string.Join(", ", r.Filters ?? [])} | {r.Scope} |");

        var notes = ResourceCatalog.All.Where(r => r.Note is not null).ToList();
        if (notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var r in notes) sb.AppendLine($"- **{r.Command}**: {r.Note}");
        }
        return sb.ToString().TrimEnd();
    }

    public const string Readme = """
        # fortnox - agent operating manual

        A CLI over the Fortnox v3 API: customers, suppliers, articles, invoices, payments, orders,
        offers, vouchers, the chart of accounts and more. Results are YAML on stdout, errors are
        YAML on stderr, and `--json` switches both to JSON. Prompts and warnings go to stderr, so
        stdout is always safe to parse.

        Field names are Fortnox's own, PascalCase, exactly as in the API documentation at
        https://api.fortnox.se/apidocs - `CustomerNumber`, `InvoiceRows`, `DocumentNumber`.

        ## Always name the account

        An account is one Fortnox company. `--account` (short `-a`) is required on every command
        that touches the API. There is no default, no "current" account and no environment
        variable - this is deliberate. One machine holds credentials for several companies, and a
        convenience default is exactly how an agent working from a summarized transcript books an
        invoice in the wrong company's ledger.

            fortnox accounts list                     # what is configured here
            fortnox customers list -a acme

        If you do not know which account to use, run `fortnox accounts list` and ask the human.
        Never guess when more than one is configured.

        ### Managing accounts

            fortnox accounts add <name> --client-id <id> [--client-secret <s>] [--service] [--scopes "..."]
            fortnox accounts add <name> --client-id <id> --client-secret <s> --tenant-id <id>
            fortnox accounts list [--check]
            fortnox accounts test <name>
            fortnox accounts remove <name> --yes [--revoke]

        `add` needs a human: it opens Fortnox's consent page in a browser and waits for the
        redirect on http://localhost:8765/callback (which must be registered on the integration).
        Do not run it yourself - give the human the command. The exception is `--tenant-id`, for a
        company that already activated the integration as a service account: that path needs no
        browser.

        Tokens refresh themselves. A user-consent account (`authMode: refresh_token`) dies if it
        goes unused for 45 days; a service account (`client_credentials`) does not.

        `remove` deletes the credentials from this machine. Without `--revoke` it does **not**
        withdraw the company's consent - say so when you report the result.

        ## Reading

            fortnox company -a <account>
            fortnox invoices list -a <account> --filter unpaid
            fortnox invoices list -a <account> --from-date 2026-01-01 --to-date 2026-03-31 --all
            fortnox invoices get 1042 -a <account>
            fortnox customers list -a <account> --query name=Acme
            fortnox vouchers get A/12 -a <account> --query financialyear=3

        Lists are paged: 100 per page by default, `--limit` up to 500, `--page N` for one page, or
        `--all` to fetch every page into one list (capped by `--max-pages`, default 50). Each page
        is one request, and Fortnox allows 25 requests per 5 seconds; the CLI backs off on 429 by
        itself. Narrow with `--filter`, dates or `--query` before reaching for `--all`.

        `--query key=value` (repeatable) passes any query parameter Fortnox supports on that
        endpoint - `customernumber`, `financialyear`, `name`, `ocr`, and so on.

        ## Changing things

            fortnox customers create -a <account> --set Name="Acme AB" --set Email=billing@acme.se
            fortnox invoices create -a <account> --data @invoice.json
            fortnox invoices update 1042 -a <account> --set YourReference="Anna"
            fortnox invoices bookkeep 1042 -a <account> --yes
            fortnox invoices pdf 1042 -a <account> -o invoice.pdf

        Bodies come from `--data` (inline JSON, `@file.json`, or `-` for stdin) plus any number of
        `--set`. Wrapping is optional: `{"Name":"Acme"}` and `{"Customer":{"Name":"Acme"}}` are
        both accepted. `--set Field=value` sends a string, `--set Field:=json` sends a number,
        boolean, object or array - ids like `CustomerNumber` are strings in Fortnox, so the plain
        form is usually right. In PowerShell, prefer `--data @file.json` over inline JSON.

        Things that will otherwise cost you:

        1. **Bookkeeping is permanent.** A booked invoice or voucher cannot be edited or deleted;
           it can only be credited or reversed with a new voucher. That is why `bookkeep`,
           `cancel`, `credit`, `email` and every `delete` refuse to run without `--yes`. Read the
           resource back and confirm with the human - including which account - before passing it.
        2. **Row arrays are replaced, not merged.** Updating `InvoiceRows` with one row removes
           every other row. Get the document, edit the full array, send it all back.
        3. **`email` sends a real email to the customer.**
        4. **`pdf` uses the preview endpoint** and does not mark the document as sent; pass
           `--mark-sent` to use `print`, which does.

        ## Anything else

            fortnox api get settings/company -a <account>
            fortnox api put invoices/1042/externalprint -a <account>
            fortnox api post articles -a <account> --data '{"Article":{"Description":"Widget"}}'

        `api` reaches every endpoint with the same account handling, token refresh, retries and
        error envelope. The body is sent exactly as given - wrap it yourself. A 403 usually means
        the integration lacks the endpoint's scope or the company lacks the license; adding a scope
        needs the human to re-run `accounts add --force --scopes "..."`.

        ## Errors

        Failures print YAML (or JSON) on stderr with a stable `code`, and exit with a matching
        status. Fortnox's own error message and code are in `error` and `detail`:

            0  ok
            1  error          unclassified - report it and stop
            2  network        retry once, then stop
            3  auth_required  stop; give the human the `remediation` string verbatim
            4  not_found      the id does not exist; do not retry
            5  rate_limited   back off before trying again
            6  invalid_input  fix the call (Fortnox validation errors land here too)
            7  no_account     run `fortnox accounts list`

        An envelope carries `remediation` when there is a specific command that fixes the
        problem. Surface it rather than guessing, and never retry an `auth_required`.
        """;
}
