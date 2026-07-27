# EVN outage notifier

A stateless, one-shot console app that checks the EVN power-outage API against
configured location rules and emails a consolidated notification for any new
matching outages. It's meant to run once per day inside a Docker container,
triggered by a host cron job - it does not schedule itself.

## Running it via Docker + cron

### 1. Get the image

Pull the published image:

```bash
docker pull jankuloskik/evn-outage-notifier
```

If you're working from a clone of this repo instead (e.g. you've made local
changes), build it instead - `docker-compose.yml` already points at the
Dockerfile, so from the repo root:

```bash
docker compose build
```

### 2. Prepare the config file

Copy the template and fill in your real values:

```bash
cp appsettings.example.json appsettings.json
```

Edit `appsettings.json` with:
- `OutageApi:Endpoint` - the real outage API URL
- `Email:*` - your SMTP host/port/credentials and sender/recipient addresses
- `MatchRules` - the `NasMesto`/`Adresa` keyword pairs you want to be notified about

`appsettings.json` is gitignored (it holds SMTP credentials) - it lives only
on the host, next to `docker-compose.yml`.

### 3. Prepare the data directory

The `data/` folder (already present in the repo) is where the SQLite database
persists across runs. No setup needed - Docker creates `data/outages.db`
inside it on first run.

### 4. Do a manual test run

From the repo root, with `appsettings.json` and `data/` next to
`docker-compose.yml`:

```bash
docker compose run --rm outage-notifier
```

Use `run --rm` (not `up`) - this is a one-shot job, and `run --rm` passes
through the container's exit code and removes the container afterward.
Check the logs printed to stdout and confirm the command's exit code is `0`.
A non-zero exit code means something failed (bad config, unreachable API,
SMTP error, etc.) - check the logged error before scheduling the cron job.

### 5. Schedule the daily cron job

Add a crontab entry (`crontab -e`) that runs once per day from the directory
containing `docker-compose.yml`, `appsettings.json`, and `data/`:

```cron
0 7 * * * cd /path/to/EVN-outage-notifier && /usr/bin/docker compose run --rm outage-notifier >> /var/log/outage-notifier.log 2>&1
```

- Adjust `0 7 * * *` to whatever time of day you want the check to run.
- Use the absolute path to `docker` (find it with `which docker`) since cron
  runs with a minimal `PATH`.
- Redirecting to a log file lets you review past runs; cron itself will
  email the invoking user on any command failure if system mail is
  configured, since a failed run exits non-zero.

That's the full loop: each day, cron starts a fresh container, it fetches
outages, emails you if there's a new match in your configured areas, records
what it sent, and exits - no process stays running between runs.
