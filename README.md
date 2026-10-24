# Funda Agent Rankings

.NET 10 console application producing the top 10 real estate agents by sale listings in Amsterdam, and separately by sale listings with a garden.

## Status

Steps 1â€“8 are implemented. All 135 test cases pass. The console is connected to the API client, ranking service, and report writer. Live API verification has not been performed; a personal key is required to run real searches.

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for the full walkthrough, decisions, and acceptance criteria. Current step: 8, console orchestration. Step 9, final integration verification, remains pending.

## Run locally

Install the .NET 10 SDK. global.json permits stable .NET 10 SDK feature bands starting at 10.0.100.

From the repository root:

```powershell
dotnet restore Funda.AgentRankings.slnx
dotnet build Funda.AgentRankings.slnx --no-restore
dotnet test Funda.AgentRankings.slnx --no-build
```

Set the personal key in the current PowerShell session using a masked prompt, then run:

```powershell
$fundaKeyInput = Read-Host 'Funda API key' -AsSecureString
$env:FUNDA_API_KEY = [System.Net.NetworkCredential]::new('', $fundaKeyInput).Password
Remove-Variable fundaKeyInput
dotnet run --project src/Funda.AgentRankings --no-build
```

The application necessarily reads the key as a string. The masked prompt avoids displaying it and entering it literally into shell history. Do not commit or log it. Remove the process environment variable when finished:

```powershell
Remove-Item Env:FUNDA_API_KEY
```

The two searches run sequentially. Both must finish before tables are printed. Progress and errors go to stderr; tables go to stdout. Press Ctrl+C to cancel. At the default pacing, searches with hundreds of pages take several minutes; retries can extend the run.

| Exit code | Meaning |
| --- | --- |
| 0 | Both searches completed and tables were printed |
| 1 | API or application failure |
| 2 | Invalid or missing configuration |
| 130 | Caller cancellation |

## Configuration

FundaOptions reads the following process environment variables. Tests need no real key and make no network requests.

| Variable | Default | Accepted values |
| --- | --- | --- |
| FUNDA_API_KEY | Required | Non-empty key without whitespace or control characters |
| FUNDA_BASE_URL | https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/ | Absolute HTTPS URL ending in `/`, without credentials, query, or fragment; omit the key |
| FUNDA_PAGE_SIZE | 25 | Integer 1â€“25; larger supported sizes are unverified |
| FUNDA_TIMEOUT_SECONDS | 30 | Integer 1â€“300; per attempt, including response buffering |
| FUNDA_REQUEST_INTERVAL_MS | 1000 | Integer 1000â€“60000 |
| FUNDA_MAX_RETRIES | 3 | Integer 0â€“5; retries after the initial attempt |

For example: `$env:FUNDA_TIMEOUT_SECONDS = '60'`. Empty optional values are rejected. Validation errors identify settings without echoing supplied values. Validation does not verify credentials or contact the API.

## Structure

- `Program.cs`: configuration, resource ownership, and Ctrl+C wiring.
- `RankingApplication`: runs both searches and handles output and exit codes.
- `Api`: HTTP listing source, validation, request pacing, retries, and safe API failures.
- `Api/Contracts`: minimal JSON contracts mapping Dutch API names to English properties.
- `Configuration`: immutable validated options from environment variables.
- `Listings`: application listing model, searches, and source interface.
- `Rankings`: aggregation independent of HTTP and console output.
- `Output`: aligned console table rendering with explicit empty results.
- `tests`: xUnit/NSubstitute tests following tests-conventions.instructions.md.

One application project and one test project keep the assignment easy to review.

## Ranking and API decisions

Count each unique listing ID once per search, grouping by agent ID. Sort by count descending, then ordinal agent name, then agent ID; return at most 10. Retain the first agent name observed. Invalid identities or fetching failures fail the run.

Count top-level Objects only. Nested ChildrenObjects are not additionally flattened; their relationship to search results remains an assumption to verify. Let type=koop and the API's /amsterdam/ or /amsterdam/tuin/ filters select listings. There is no explicit garden flag in the supplied sample.

Objects[].Id, MakelaarId, and MakelaarNaam provide identity. Paging.HuidigePagina and Paging.AantalPaginas provide page numbers. VolgendeUrl is a website-relative path and is not followed. Unneeded fields such as photos, prices, and legacy dates are ignored. Required properties detect missing fields; client validation handles explicit nulls and invalid values.

The sample contains 25 objects, page 1 of 226, with 5,631 total objects. These are sample values, not current results. AccountStatus zero is the only verified successful status; unknown statuses fail conservatively. Other error payload formats remain unverified.

The client rejects unexpected empty pages, pages containing only previously seen listing IDs, and changes to the total page count. Duplicate IDs on otherwise progressing pages are handled by aggregation. Listings may change during pagination: deduplication cannot guarantee a consistent snapshot or recover listings missed when pages shift.

## Pacing and retries

One policy serializes requests across both searches and spaces every attempt by the configured interval, including retries. HTTP 408, 429, 500, 502, 503, and 504, transport failures, and non-caller timeouts are retried. Other HTTP statuses and invalid response data fail immediately.

Backoff starts at one second, doubles per retry, and adds up to 500 ms of jitter. Retry-After supports both seconds and HTTP dates; the longer of backoff and server delay is used, followed by the normal pacing check. Waits support cancellation. Each attempt has a fresh request and timeout. The request budget is shared within the process, not across independent runs using the same key.

Program disables automatic redirects to prevent implicit unpaced requests. HttpClient's overall timeout is disabled; the policy applies the per-attempt timeout without counting pacing and backoff. API errors omit raw transport/parser exceptions, response bodies, and request URLs. Unexpected application errors use generic output to avoid exposing credentials.

Tests use virtual timing and in-memory transport. Offline orchestration tests verify that a failed second search prints no partial ranking report. Output-device failures can still interrupt rendering. A missing-key smoke check verifies the real entry point returns exit code 2 without requests. Live success and interactive Ctrl+C verification remain pending.

## Typed HTTP client

AddFundaApi registers IListingSource with FundaClient through AddHttpClient<IListingSource, FundaClient>. Microsoft.Extensions.Http supplies IHttpClientFactory and manages handler pooling. Registration configures the HTTPS base address, disables redirects and the overall HttpClient timeout, and removes request loggers because the URL path contains the API key.

FundaOptions and FundaRequestPolicy are singletons; typed clients are transient. Every resolved client receives the same request policy so creating another client does not reset pacing. Program resolves the listing source from a disposable service provider. No generic host is needed for this console app.

This refactor does not change JSON contracts. The supplied page-2 response subsequently confirmed a grouped parent object without MakelaarId; see the grouped identity rule below.
## Grouped objects without an agent ID

The supplied page-2 response contains a top-level object with no MakelaarId property and a child with a valid agent ID and matching agent name. The API contract now allows a missing or null parent ID. ToListing resolves it only when every child has the same positive agent ID and exactly matches the parent agent name. The parent listing ID is retained and counted once; children are not additionally counted. An explicitly invalid parent ID or ambiguous/missing child evidence fails the operation with a safe page/object diagnostic. This fallback is an application rule based on the supplied response, not a documented API guarantee.

Nine regression cases cover page-2 recovery (missing and null parent IDs), counting the parent once, and rejecting missing/invalid/conflicting child evidence. These cases pass. The current full run has 137 passing and 7 failing cases; those seven failures concern an existing local hardcoded key in FundaOptions.Load overriding the environment reader. That local edit was preserved. Use FUNDA_API_KEY through the documented environment reader to restore configuration test isolation.