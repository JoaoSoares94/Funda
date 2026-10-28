# Funda Agent Rankings

A .NET 10 console application that prints two top-10 tables of real estate agents
ranked by unique property listings for sale:

1. All properties in Amsterdam.
2. Properties in Amsterdam with a garden.

## Run locally

Install a stable .NET 10 SDK. [global.json](global.json) permits SDK feature-band
roll-forward from 10.0.100 within .NET 10.

From the repository root, restore dependencies and run the tests:

```powershell
dotnet restore Funda.AgentRankings.slnx
dotnet test Funda.AgentRankings.slnx --no-restore
```

Supply your temporary Funda API key through an environment variable. This masked
prompt avoids displaying the key or entering it literally in shell history:

```powershell
$fundaKeyInput = Read-Host 'Funda API key' -AsSecureString
```
After this step you should input your API key, and after that, you can then use it as an env variable using the following commands:
```powershell
$env:FUNDA_API_KEY = [System.Net.NetworkCredential]::new('', $fundaKeyInput).Password
Remove-Variable fundaKeyInput
dotnet run --project src/Funda.AgentRankings --no-restore
```

Remove the environment variable when finished:

```powershell
Remove-Item Env:FUNDA_API_KEY
```

Keep the submitted settings file key-free. Local credentials under `.local/` and
build artifacts should be excluded from the submission.

The introduction and progress bars use stderr; ranking tables use stdout. Both
searches must succeed before tables are printed. Press Ctrl+C to cancel.
Redirected or unsupported terminals receive a final plain percentage summary
for each started search instead of live redraws.

## Configuration

Non-secret settings live in
[appsettings.json](src/Funda.AgentRankings/appsettings.json), under `Funda`.
The file is loaded relative to the application directory and copied to build and
publish output. Environment variables override JSON; use double underscores for
nested settings, for example:

```powershell
$env:Funda__PageSize = '10'
$env:Funda__EnableAgentNameFallback = 'true'
```

| Setting | Default | Validation |
| --- | --- | --- |
| `BaseUrl` | Assignment's HTTPS JSON feed endpoint | Absolute HTTPS URL ending in `/`; no credentials, query or fragment |
| `PageSize` | 25 | 1-25 |
| `TimeoutSeconds` | 30 | 1-300, per HTTP attempt |
| `RequestIntervalMs` | 650 | 650-60000 |
| `MaxRetries` | 3 | 0-5 retries after the initial attempt |
| `EnableAgentNameFallback` | false | Boolean |
| `ApiKey` | Empty; supply externally | Required; no whitespace or control characters |

`FUNDA_API_KEY` takes precedence over `Funda:ApiKey`, including a section-based
environment override. Options validation runs during Generic Host startup, before
the API client is resolved. Configuration errors omit supplied sensitive values.

## Design decisions

### Counting and agent identity

- Enumerate every parent and child, including deeper descendants. Merge all
  occurrences by property ID across the complete page range before counting.
  GUID identifiers are normalized so casing cannot cause double-counting.
- Sort by listing count descending, then agent name using ordinal comparison,
  then agent ID. Return at most 10 agents and retain the first name observed.
- Prefer each property's own supplied `MakelaarId`. A later occurrence can fill
  a missing ID; direct identity always takes precedence over inference. Conflicting
  direct IDs for the same property leave it unresolved, regardless of arrival order.
- When a property has no direct agent ID, infer one only if its children's known
  direct IDs agree and none of those children has conflicting direct identities.
  Children retain their own IDs and agents; a parent's agent is not copied down.
- Missing names do not discard an identified property: use a known name for its
  agent ID or the display label `Agent <id>`. Missing/empty property IDs and supplied
  non-positive agent IDs fail the search explicitly instead of silently losing data.
- Optional name fallback is disabled by default. When enabled, defer unresolved
  entries until all pages have been read, then match trimmed, case-insensitive
  names to exactly one known ID from the same search. Unknown or ambiguous names
  remain unresolved. Only nonconflicting, directly supplied identities establish
  name matches; inferred identities do not establish further matches.

Every identified property has an outcome: ranked or unresolved. Completion summaries
show both counts, conflicting direct identities, and inferred identities. Child-based
and name-based resolution remain heuristics. Eligibility follows the chosen API
query; the algorithm does not inspect or infer garden attributes locally.

### Fetching and resilience

The two searches run sequentially using `type=koop` and the API's `/amsterdam/`
and `/amsterdam/tuin/` filters. The first response determines the page range.
Each response's current page must match the requested page. Empty intermediate
pages and changes to later page totals do not change the original range.

A shared request policy spaces every attempt, including retries, by at least
650 ms by default: approximately 92 requests per minute. Elapsed response time
counts toward the interval. The budget is per process; separate instances using
the same key are not coordinated.

`Microsoft.Extensions.Http.Resilience` supplies retry and timeout strategies:

- Retry HTTP 408, 429, 500, 502, 503 and 504, transient transport failures, and
  attempt timeouts. Caller cancellation is not retried.
- Use exponential backoff from one second with package-managed jitter, or honor
  the server's `Retry-After` delta/date when supplied.
- Apply timeout to each buffered HTTP attempt, including response-body reading.
  Pacing and retry backoff are outside that timeout. Zero retries still retains
  pacing and timeout.

Requests use fresh messages on retry. Automatic redirects and HTTP-client request
logging are disabled because the API key is part of the URL. API errors omit raw
request URLs, response bodies and inner transport/parser exceptions.

### Structure and presentation

One application project and one test project keep the solution easy to navigate:

| Area | Responsibility |
| --- | --- |
| `Program.cs` | Host startup, configuration, dependency wiring and cancellation |
| `Configuration` | Typed settings and Options validation |
| `Api` | Typed HTTP client, pagination, identity resolution and resilience |
| `Listings` / `Rankings` | Listing contracts and aggregation through `IListingSource` |
| `Output` | Spectre.Console introduction, progress bars and report tables |

Progress reports fractional completion after every page and reaches 100% only
after enumeration and optional name matching finish. The live display ends before
tables or errors are written. Tables use ASCII borders and literal text cells;
control characters in agent names become spaces.
Completion summaries account for all unique property IDs, including unresolved
agents. A failed page aborts the search; partial rankings are not emitted.

## Verification and limitations

The full Amsterdam findings, identity ambiguities, remaining unresolved properties,
and ranking comparisons are documented in [FINDINGS.md](FINDINGS.md).

## AI assistance

OpenAI Codex assisted with planning, C# implementation and refactoring, test design
and consolidation, and documentation. This included API mapping and pagination,
agent identity resolution, request pacing/retries/timeouts, configuration and host
startup, and Spectre.Console output. It was used to compare approaches, explain
tradeoffs and implement incremental changes following developer feedback and
supplied API examples. Verification used automated tests, offline startup checks,
a read-only audit of all Amsterdam search pages, and offline replay of cached
Amsterdam and garden responses through the client and ranking service. End-to-end
interactive application verification remains unrecorded.

