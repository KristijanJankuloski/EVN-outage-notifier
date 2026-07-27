# Outage Notifier — Initial Scaffold Design

Date: 2026-07-27

## Context

The project currently contains only the default Visual Studio console scaffold
(`Program.cs` prints "Hello, World!"). `CLAUDE.md` describes the target
architecture: a stateless, one-shot .NET console app that runs once per day in
Docker, checks a power-outage API against configured location rules, and
emails a consolidated notification for any new matching outages. This spec
turns that target description into a concrete implementation design.

## Goals

- Implement the full execution flow described in `CLAUDE.md`: config load →
  SQLite schema ensure → API fetch → match against rules → send one email →
  persist notified outage IDs (only after successful send).
- Keep each responsibility in its own small, independently testable component.
- Correct exit codes (`0` success, non-zero on any failure) so an external
  cron/scheduler can detect failed runs.
- Deployable via Docker Compose with the config file and SQLite data
  bind-mounted from the host.

## Non-goals

- No in-app scheduling — an external host cron drives runs.
- No test project in this pass (explicitly deferred; components are shaped so
  tests are easy to add later).
- No support for the real EVN API's actual auth/quirks — the design targets
  the JSON shape already documented in `CLAUDE.md`, configurable purely via
  `appsettings.json`.

## Project layout

```
OutageNotifier/OutageNotifier/
  Program.cs
  appsettings.json
  Configuration/
    AppOptions.cs        # OutageApiOptions, DatabaseOptions, EmailOptions, MatchRule
  Models/
    Outage.cs             # prekinID, kecId, tipPrekin, nasMesto, adresa, pocetok, kraj, napNivo
  Services/
    IOutageApiClient.cs / OutageApiClient.cs
    INotifiedOutageStore.cs / SqliteNotifiedOutageStore.cs
    IOutageMatcher.cs / OutageMatcher.cs
    IEmailSender.cs / MailKitEmailSender.cs
    OutageNotifierRunner.cs
docker-compose.yml          # repo root
```

Each service sits behind a small interface so `OutageNotifierRunner` can be
tested against fakes later without touching real HTTP/SQLite/SMTP.

## Stack choices

| Concern            | Choice                                            |
|--------------------|----------------------------------------------------|
| Host/DI/config      | `Host.CreateApplicationBuilder` (Generic Host)     |
| Logging             | Serilog, Console sink, compact JSON output          |
| HTTP                | `AddHttpClient<IOutageApiClient, OutageApiClient>`, 30s timeout |
| JSON                | `System.Text.Json` with `JsonPropertyName` mappings |
| SQLite access       | `Microsoft.Data.Sqlite` + raw SQL (no ORM)          |
| Email               | MailKit over SMTP                                   |
| Email body          | Simple HTML (one row/block per matched outage)      |

## Configuration (`appsettings.json`)

Single mounted file holding everything, including SMTP credentials (accepted
trade-off for simplicity — the file lives outside the image on a bind mount
the operator controls):

```json
{
  "OutageApi": {
    "Endpoint": "https://example.com/api/outages"
  },
  "Database": {
    "ConnectionString": "Data Source=/data/outages.db"
  },
  "Email": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "UseStartTls": true,
    "Username": "...",
    "Password": "...",
    "From": "outage-notifier@example.com",
    "To": ["outage-reciver@example.com"],
    "Subject": "EVN Outage Notification"
  },
  "MatchRules": [
    { "NasMesto": "СКОПЈЕ", "Adresa": "ВОДНЈАНСКА" },
    { "NasMesto": "КУМАНОВО" }
  ]
}
```

`MatchRule.NasMesto` and `MatchRule.Adresa` are both nullable strings. At
least one must be set per rule — validated at startup; a rule with both null
is a configuration error (fail fast, exit 1), not a wildcard match-everything
rule.

## Execution flow

1. Configure Serilog first (console sink, JSON), before anything else can
   throw.
2. Bind `appsettings.json` into strongly-typed options and validate them
   (endpoint present, connection string present, SMTP fields present, each
   match rule has ≥1 of `NasMesto`/`Adresa`). Validation failure → log and
   exit 1 immediately.
3. `SqliteNotifiedOutageStore` opens the configured SQLite file and runs:
   ```sql
   CREATE TABLE IF NOT EXISTS NotifiedOutages (
     PrekinId INTEGER PRIMARY KEY,
     NotifiedAtUtc TEXT NOT NULL
   );
   ```
4. `OutageApiClient` issues an HTTP GET against `OutageApi:Endpoint` and
   deserializes the JSON array into `List<Outage>`.
5. `OutageNotifierRunner` filters out any outage whose `PrekinId` already
   exists in the store, then evaluates each remaining outage against every
   configured `MatchRule` via `OutageMatcher`:
   - a rule matches when `nasMesto` contains the rule's `NasMesto` (if set)
     and `adresa` contains the rule's `Adresa` (if set) — case-insensitive,
     substring match.
   - an outage matching any rule is included exactly once in the result set
     (first match wins; it is not duplicated per matching rule).
6. If the matched set is empty: log "no new matches" and finish successfully
   — no email sent, nothing written to the store.
7. If non-empty: `MailKitEmailSender` builds one HTML email listing every
   matched outage (type, `NasMesto`, `Adresa`, `NapNivo`, start/end) and sends
   it via SMTP using the configured credentials.
8. Only after the send succeeds, the store writes all matched `PrekinId`s
   with the current UTC timestamp in a single transaction. If the send
   throws, nothing is written — the next run will retry those same outages.
   This ordering must not be reversed.

## Error handling & exit codes

- A single top-level try/catch around the whole run in `Program.cs` (or
  around `OutageNotifierRunner.RunAsync`) catches any exception from any
  stage, logs it with a stage-identifying message, and results in exit code
  `1`.
- No in-process retries — the next day's cron-triggered run is the retry
  mechanism, enabled by the send-then-persist ordering above.
- The HTTP client has a 30-second timeout so a hung API cannot wedge the
  container indefinitely.

## Deployment

`docker-compose.yml` at the repo root:

```yaml
services:
  outage-notifier:
    build:
      context: .
      dockerfile: OutageNotifier/OutageNotifier/Dockerfile
    volumes:
      - ./appsettings.json:/app/appsettings.json:ro
      - ./data:/data
```

- `appsettings.json` is bind-mounted read-only from the repo root so it can
  be edited on the host without rebuilding the image.
- `./data` is bind-mounted to `/data`, holding `outages.db`, matching the
  `Data Source=/data/outages.db` connection string, so it survives container
  recreation.
- A host cron job runs `docker compose run --rm outage-notifier` once a day;
  the app itself remains oblivious to this scheduling and is runnable
  standalone via `dotnet run` or `docker run` at any time.

## Testing strategy (for later)

No test project is added in this scaffolding pass. When one is introduced:

- `OutageMatcher` — pure function, no I/O; unit-testable directly for
  contains/case-insensitivity/omitted-field behavior.
- `OutageNotifierRunner` — tested against fake `IOutageApiClient` /
  `INotifiedOutageStore` / `IEmailSender` to verify the skip-already-notified
  filter and the send-then-persist ordering (including the "email fails →
  nothing persisted" case).
- `SqliteNotifiedOutageStore` — tested against a real temp-file SQLite
  database (fast enough that mocking isn't worth it).
