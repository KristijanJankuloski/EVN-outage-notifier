# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

The project skeleton has been scaffolded via Visual Studio but contains no implementation yet
(`Program.cs` is still the default "Hello, World!" template). The sections below describe the
target design from the project spec — use them as the implementation blueprint, not as a
description of existing code.

## Commands

Solution/project layout:
- Solution: `OutageNotifier/OutageNotifier.slnx`
- Project: `OutageNotifier/OutageNotifier/OutageNotifier.csproj` (net10.0, console app)

```
dotnet build OutageNotifier/OutageNotifier.slnx        # build
dotnet run --project OutageNotifier/OutageNotifier      # run locally
dotnet publish OutageNotifier/OutageNotifier -c Release # publish
```

Docker (multi-stage build defined in `OutageNotifier/OutageNotifier/Dockerfile`):
```
docker build -f OutageNotifier/OutageNotifier/Dockerfile -t outage-notifier .
```

There is no test project yet; add one under the solution when tests are introduced.

## Architecture (target design)

The Outage Notifier is a **stateless, one-shot console app** meant to run once per day inside a
Docker container, triggered by an external scheduler (host cron). It does not implement its own
scheduling — it runs, does its work, and exits. No process stays resident between executions.

Execution flow on every run:
1. Initialize logging (structured, written to stdout so Docker captures it).
2. Load configuration from a mounted `appsettings.json`: API endpoint, SQLite connection string,
   email settings, and a list of location matching rules (`NasMesto` / `Adresa` keyword pairs,
   either of which may be omitted).
3. Open the SQLite database (file lives on a mounted Docker volume so it persists across
   container runs) and ensure the schema exists: a single table keyed by outage `PrekinID`,
   storing the `PrekinID` and the timestamp the notification was sent.
4. HTTP GET the configured outage API endpoint; deserialize the JSON array response into typed
   models (`prekinID`, `kecId`, `tipPrekin`, `nasMesto`, `adresa`, `pocetok`, `kraj`, `napNivo`).
5. For each outage not already present in the database, evaluate it against every configured
   matching rule: a rule matches when `nasMesto` contains the rule's `NasMesto` keyword (if set)
   and `adresa` contains the rule's `Adresa` keyword (if set) — case-insensitive, partial-string
   ("contains") matching, not exact match. Outages already recorded, or matching no rule, are
   skipped.
6. If no new matches were found, log that fact and exit 0 without sending email.
7. If matches were found, build and send **one** consolidated email covering all of them
   (type, `NasMesto`, `Adresa`, `NapNivo`, start/end time, etc.).
8. Only after successful email delivery, write every notified `PrekinID` + timestamp to the
   database. If email delivery fails, nothing is recorded, so the next run retries those outages
   — this ordering (send, then persist) is the mechanism that prevents lost or duplicate
   notifications and should not be reordered.

Exit codes matter: `0` is success; any failure (config, API, email, database) must return
non-zero so the external cron/scheduler can detect a failed run.

Deployment model: a host cron job starts a fresh container once per day, mounting the config
file and the SQLite volume, and removes the container after it exits — only the SQLite database
and logs persist. The app itself must stay oblivious to this scheduling and remain runnable
standalone via `dotnet run` or `docker run` at any time.
