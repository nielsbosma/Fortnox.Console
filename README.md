# Fortnox CLI

A command-line tool for the [Fortnox](https://www.fortnox.se) API. Work with customers, invoices,
payments, vouchers, the chart of accounts and more from the terminal. It is built to be driven by an
LLM agent as well as by people.

## Installation

```bash
dotnet tool install -g Fortnox.Console
```

## Output

Commands print YAML by default. Pass `--json` for raw JSON, which is what you want when scripting:

```bash
fortnox invoices get 1042 -a acme --json | jq -r .Invoice.Balance
```

Field names are Fortnox's own, as in the [API reference](https://api.fortnox.se/apidocs):
`CustomerNumber`, `DocumentNumber`, `InvoiceRows`.

## Accounts

Each account is one Fortnox company. Fortnox has no API keys; access goes through an
**integration** that you register in the [Fortnox developer portal](https://developer.fortnox.se).
The integration has a client id and a client secret. Register
`http://localhost:8765/callback` as its redirect URI, and enable the scopes you need.

Add a company once. A browser opens on Fortnox's consent page:

```bash
fortnox accounts add acme --client-id <id>                # prompts for the secret, opens the browser
fortnox accounts add acme --client-id <id> --service      # authorize as a service account
fortnox accounts add acme --client-id <id> --client-secret <s> --tenant-id 1234567
```

- **Default (user consent).** The CLI stores a refresh token and rotates it automatically. Fortnox
  expires it after **45 days without use**. After that, run `add --force` again.
- **`--service`.** A system administrator authorizes the integration as a service account. After
  that the CLI gets tokens with client credentials and never needs a refresh token. Use this for
  anything long-lived.
- **`--tenant-id`.** For a company that has already activated the integration as a service
  account. It needs no browser.

`add` reads `companyinformation` before it stores anything, so bad credentials fail at this step and
not on a later command. The client secret and tokens go into the OS keystore: DPAPI on Windows,
Keychain on macOS, libsecret on Linux. Only the name, client id, tenant id, company name and
organization number are written to `config.yaml`.

Then name the account on every command:

```bash
fortnox invoices list -a acme --filter unpaid
```

There is no default account and no `FORTNOX_TOKEN` variable. With several companies on one machine,
a default is how an invoice gets booked in the wrong company.

```bash
fortnox accounts list [--check]     # --check calls the API once per account
fortnox accounts test acme          # which company this is, and whether it still works
fortnox accounts remove acme        # deletes local credentials; --revoke also revokes the refresh token
```

| Option | Default |
| --- | --- |
| `--scopes "..."` | every scope the built-in commands use |
| `--redirect-uri` | `http://localhost:8765/callback` |
| `--no-browser` | prints the URL instead of opening it |

| Variable | Effect |
| --- | --- |
| `FORTNOX_CONFIG_DIR` | Overrides where `config.yaml` and the secret store live |
| `FORTNOX_SECRET_STORE` | Forces a backend: `dpapi`, `keychain`, `libsecret`, `plaintext` |
| `FORTNOX_ALLOW_PLAINTEXT_STORE=1` | Permits a 0600 file where no OS keystore exists |

## Usage

```bash
fortnox <resource> <verb> [ID] [options] -a <account>
```

Every resource has the same verbs. Which verbs a resource has depends on what the API allows:

| Resource | Verbs |
| --- | --- |
| `customers`, `suppliers`, `articles`, `projects`, `cost-centers`, `units`, `currencies`, `terms-of-payments` | list, get, create, update, delete |
| `invoices` | list, get, create, update, pdf, bookkeep, cancel, credit, email, einvoice, external-print, warehouse-ready |
| `invoice-payments`, `supplier-payments` | list, get, create, update, delete, bookkeep |
| `supplier-invoices` | list, get, create, update, bookkeep, cancel, credit, approve-payment, approve-bookkeep |
| `orders` | list, get, create, update, pdf, create-invoice, cancel, email |
| `offers` | list, get, create, update, pdf, create-order, create-invoice, cancel, email |
| `vouchers` | list, get, create. Ids are `SERIES/NUMBER`, e.g. `A/12` |
| `voucher-series`, `ledger-accounts`, `price-lists` | list, get, create, update |
| `financial-years` | list, get, create |

Other commands:

| Command | Description |
| --- | --- |
| `company` | Company information for the account |
| `api get\|post\|put\|delete <path>` | Call any v3 endpoint directly |
| `agent-readme` | The operating manual for an LLM agent |

### Listing

```bash
fortnox invoices list -a acme --filter unpaidoverdue
fortnox invoices list -a acme --from-date 2026-01-01 --to-date 2026-03-31 --all
fortnox customers list -a acme --query name=Acme --limit 500
```

Lists return one page at a time: 100 items by default, and at most 500 with `--limit`. `--all`
fetches every page and returns one list. Fortnox allows 25 requests per 5 seconds; when it answers
429, the CLI backs off and retries.

### Writing

```bash
fortnox customers create -a acme --set Name="Acme AB" --set Email=billing@acme.se
fortnox invoices create -a acme --data @invoice.json
fortnox invoices update 1042 -a acme --set YourReference=Anna
fortnox articles update A1 -a acme --set SalesPrice:=199.5 --set Active:=true
```

`--data` takes inline JSON, `@file.json` or `-` for stdin. The body can be wrapped
(`{"Customer":{...}}`) or not. `--set Field=value` sends a string, and `--set Field:=json` sends a
number, boolean, object or array. In PowerShell, use `--data @file.json` to avoid quoting problems.

## Notes on behaviour

**Irreversible actions need `--yes`.** These are `bookkeep`, `cancel`, `credit`, `email`,
`einvoice`, the `approve-*` actions, every `delete`, and `api delete`. You cannot edit or delete a
booked document. You can only credit it or reverse it with a new voucher.

**Updates replace row arrays.** Fortnox merges the top-level fields of a PUT, but it replaces
`InvoiceRows` and similar arrays completely. If you send one row, every other row is removed.

**`pdf` does not mark a document as sent.** It uses the `preview` endpoint. Pass `--mark-sent` to
use `print` instead.

**A 403 usually means a missing scope or license.** Re-authorize with
`accounts add <name> --force --scopes "..."`, or check what the company subscribes to.

## Driving this from an agent

```bash
fortnox agent-readme           # the operating manual, as markdown
fortnox agent-readme --json    # the same rules, resources and exit codes as data
```

Errors print a YAML (or JSON) envelope on stderr with a stable `code`, and the exit status matches
it:

| Exit status | Code |
| --- | --- |
| `1` | error |
| `2` | network |
| `3` | auth_required |
| `4` | not_found |
| `5` | rate_limited |
| `6` | invalid_input |
| `7` | no_account |

Fortnox's own error message and code are in `error` and `detail`. When a specific command fixes the
problem, the envelope also carries `remediation`.
