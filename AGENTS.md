# AGENTS.md

Notes for whoever extends this next.

`Fortnox.Console` is a .NET 10 global tool (`fortnox`) over the Fortnox v3 REST API, built to be
driven by an LLM agent. It follows the house conventions in `../ConsoleGuidelines.md`, and its
skeleton follows `../Sliplane.Console`: accounts in the OS keystore, a required `--account`, YAML
output by default, and an error envelope whose `code` doubles as the exit status. Read Sliplane's
AGENTS.md too; the traps it lists apply here unchanged.

The manual that the *agent* reads comes from `fortnox agent-readme`, in
`src/Commands/AgentReadmeCommand.cs`. Its resource table is generated from `ResourceCatalog.All`.

## Commands

```bash
dotnet build src/Fortnox.Console.csproj -c Release
dotnet src/bin/Release/net10.0/Fortnox.Console.dll <args>

dotnet pack src/Fortnox.Console.csproj -c Release -p:Version=0.1.0 --output ./nupkgs
dotnet tool install --global --add-source ./nupkgs Fortnox.Console --version 0.1.0
```

`dotnet build` does not update the installed `fortnox`, so repack and reinstall after a change.
Bump the version every time, because NuGet caches by version.

Test against a throwaway config so real credentials are never touched:

```powershell
$env:FORTNOX_CONFIG_DIR = "$env:TEMP\fortnox-test"
$env:FORTNOX_SECRET_STORE = "plaintext"   # lets you hand-write secrets.json for a fake account
```

## Layout

```
src/
  Program.cs                   registration; one branch per resource, generated in a loop
  Infrastructure/
    Resource.cs                ResourceCatalog: every resource, its id field, scope, verbs, actions
    FortnoxClient.cs           HTTP, 401 -> refresh once, 429 -> backoff, ErrorInformation parsing
    Body.cs                    --data / --set -> wrapped JSON body
    ResourcePath.cs            id escaping (keeps "/" for vouchers), --query, the --yes guard
    ApiSettings.cs, ErrorOutput.cs, Output.cs, FortnoxException.cs   as in Sliplane
  Auth/
    FortnoxOAuth.cs            token endpoint: auth code (+PKCE), refresh, client credentials, revoke
    BrowserLogin.cs            loopback HttpListener for the authorization-code redirect
    TokenProvider.cs           cached access token, refresh under a cross-process lock
    AccountResolver.cs, FortnoxIdentity.cs, Jwt.cs
  Storage/                     as in Sliplane, plus AccountSecrets (one JSON blob per account)
  Commands/
    Accounts/                  add, list, test, remove
    Resources/                 generic list/get/create/update/delete/pdf/action, bound via WithData
    ApiCommand.cs              raw passthrough
```

## How resources work

There is no command class per resource. `Program.cs` loops over `ResourceCatalog.All` and registers
the generic commands with the `Resource` (or an `ActionData`) attached through Spectre's
`WithData`. Each command reads it back from `context.Data`. To add a resource or an action, add one
entry to `ResourceCatalog`. The help text, the `agent-readme` table and the default OAuth scopes all
come from that entry. Also update the table in `README.md`.

Fortnox wraps every payload. Lists come back as `{"Customers":[...],"MetaInformation":{...}}`, one
object as `{"Customer":{...}}`, and writes expect the singular wrapper. `Resource.Plural` and
`Resource.Singular` must match those keys exactly. `--all` relies on `Plural`. One irregular case is
voucher series, which uses `VoucherSeriesCollection` as its plural.

A resource with an unusual shape gets its own command class rather than more flags on the generic
ones.

## Invariants

**`--account` is required everywhere.** The reasoning is the same as in Sliplane.
`AccountResolver.Resolve` is the only way that credentials enter the process.

**Irreversible verbs need `--yes`.** Bookkeeping in Fortnox is permanent, and `email` reaches a
real customer. Sliplane deletes without asking; this tool does not, on purpose. A new action whose
effect cannot be undone gets `Irreversible: true`.

**Refresh tokens rotate, and refreshing happens under `ConfigStore.TokenLockPath`.** Using a
refresh token invalidates it. If two processes refreshed from the same stored token, the one that
lost the race would hold a dead token, and the account would need a human to re-authorize it.
`TokenProvider` re-reads the stored token inside the lock, so a process that waited picks up the
token that the winner stored. Keep the read-refresh-write cycle in one lock and one secrets write.
`TokenLockPath` is separate from `LockPath` because `FileLock` is not reentrant.

**Secrets never touch `config.yaml`.** The client secret, refresh token and access token live in
the `AccountSecrets` blob in the keystore.

## Fortnox API notes

- Auth: `https://apps.fortnox.se/oauth-v1/{auth,token,revoke}`. Every call to the token endpoint
  uses HTTP Basic `client_id:client_secret`. Access tokens last 1 hour. Refresh tokens last 45
  days and rotate on each use.
- Client credentials: `grant_type=client_credentials` plus a `TenantId` header. This only works
  after the company has consented with `account_type=service`. The tenant id is the `tenantId`
  claim in any access token, and it is also `DatabaseNumber` in `companyinformation`.
- API: `https://api.fortnox.se/3/`. Errors are
  `{"ErrorInformation":{"error":1,"message":"...","code":2000359}}`, and the casing of the inner
  keys varies.
- Rate limit: 25 requests per 5-second sliding window, per token.
- Paging: `?page=&limit=` (at most 500), with `MetaInformation.@TotalPages`.
- Scopes and the licenses they need are listed at
  https://www.fortnox.se/developer/guides-and-good-to-know/scopes. Asking for a scope that is not
  enabled on the integration fails the consent.

## Not verified against a live company

The OAuth error paths and the loopback listener have been exercised against the real endpoints
with dummy credentials. A full consent, the action endpoints and the PDF endpoints have not yet been
run against a real Fortnox company. Check them the first time you use them.

## Releasing

CI builds every push to `main`. Publishing a GitHub Release `vX.Y.Z` packs the tool and pushes it to
NuGet using the `NUGET_API_KEY` repository secret.
