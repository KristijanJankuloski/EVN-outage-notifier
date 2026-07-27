# Outage Notifier Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the full Outage Notifier one-shot console app described in `docs/superpowers/specs/2026-07-27-outage-notifier-scaffold-design.md` — config-driven outage fetch, dedup-and-match, consolidated email, and send-then-persist tracking — plus its Docker Compose deployment.

**Architecture:** A Generic Host (`Host.CreateApplicationBuilder`) wires Serilog logging, strongly-typed options bound from `appsettings.json`, and small single-responsibility services (API client, SQLite store, matcher, email sender) behind interfaces, orchestrated by one `OutageNotifierRunner`. `Program.cs` validates config at startup, runs the orchestrator once, and maps any failure to a non-zero exit code.

**Tech Stack:** .NET 10 console app, Microsoft.Extensions.Hosting, Serilog (Console sink, compact JSON), Microsoft.Data.Sqlite (raw SQL, no ORM), MailKit (SMTP), System.Text.Json.

## Global Constraints

- Target framework: `net10.0`, `OutputType=Exe` (already set in `OutageNotifier/OutageNotifier/OutageNotifier.csproj` — do not change).
- No test project in this pass — this is an explicit, approved decision from the design spec, not an oversight. Each task is verified by `dotnet build` and, where noted, a manual `dotnet run` / `docker build` check instead of automated tests.
- Do not create any git commits or pushes while executing this plan — the user will handle git themselves. Skip any "commit" step you might otherwise expect at the end of a task.
- Send-then-persist ordering is load-bearing: the email must be sent successfully **before** `NotifiedOutages` rows are written. Never reorder this, even for convenience.
- Matching is case-insensitive, substring ("contains") matching — not exact match. A `MatchRule` needs at least one of `NasMesto`/`Adresa` set; a rule with neither is a configuration error.
- Exit code `0` = success (including "no new matches"); any failure (config, HTTP, SQLite, SMTP) = exit code `1`.
- All secrets (SMTP username/password) live in `appsettings.json` per the approved design — no environment-variable override layer.
- Whenever a task creates a new source folder for the first time (`Models/`, `Configuration/`, `Services/`), add a matching `<Folder Include="...\" />` item to `OutageNotifier.csproj` so it appears in Visual Studio's Solution Explorer.

---

### Task 1: Add required NuGet packages

**Files:**
- Modify: `OutageNotifier/OutageNotifier/OutageNotifier.csproj`

**Interfaces:**
- Produces: package references consumed by every later task (`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.Console`, `Serilog.Formatting.Compact`, `Microsoft.Data.Sqlite`, `MailKit`).

- [ ] **Step 1: Add the packages**

Run from the repo root:

```bash
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Microsoft.Extensions.Hosting
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Microsoft.Extensions.Http
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Serilog.Extensions.Hosting
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Serilog.Sinks.Console
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Serilog.Formatting.Compact
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package Microsoft.Data.Sqlite
dotnet add OutageNotifier/OutageNotifier/OutageNotifier.csproj package MailKit
```

- [ ] **Step 2: Verify the project restores and builds**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.` with the new `<PackageReference>` entries visible in `OutageNotifier.csproj`.

---

### Task 2: Outage model

**Files:**
- Create: `OutageNotifier/OutageNotifier/Models/Outage.cs`
- Modify: `OutageNotifier/OutageNotifier/OutageNotifier.csproj`

**Interfaces:**
- Produces: `OutageNotifier.Models.Outage` — consumed by every service task below.

- [ ] **Step 1: Write the model**

```csharp
using System.Text.Json.Serialization;

namespace OutageNotifier.Models;

public sealed class Outage
{
    [JsonPropertyName("prekinID")]
    public string PrekinId { get; set; } = string.Empty;

    [JsonPropertyName("kecId")]
    public int? KecId { get; set; }

    [JsonPropertyName("tipPrekin")]
    public string? TipPrekin { get; set; }

    [JsonPropertyName("nasMesto")]
    public string? NasMesto { get; set; }

    [JsonPropertyName("adresa")]
    public string? Adresa { get; set; }

    [JsonPropertyName("pocetok")]
    public DateTimeOffset? Pocetok { get; set; }

    [JsonPropertyName("kraj")]
    public DateTimeOffset? Kraj { get; set; }

    [JsonPropertyName("napNivo")]
    public string? NapNivo { get; set; }
}
```

**Post-scaffold correction:** the real EVN API returns `prekinID` as a GUID string (e.g. `"469a08a7-c000-433d-9cef-b200767464ed"`), not an integer, and `kecId` as a real JSON number - the reverse of what was originally assumed here from the field list in `CLAUDE.md` alone. Confirmed directly against the live endpoint (`https://portal-api.elektrodistribucija.mk/DSO/Prekini/ZemiPrekini`) across all 129 records at the time of testing. `PrekinId` is `string`, `KecId` is `int?`; `INotifiedOutageStore`/`SqliteNotifiedOutageStore` (Task 5) use `string` for `PrekinId` throughout (`TEXT PRIMARY KEY` column, `GetString` instead of `GetInt32`) to match.

- [ ] **Step 2: Register the folder in the csproj so it shows up in Visual Studio**

`.cs` files under a new folder still compile fine without this (SDK-style projects glob `**/*.cs` automatically), but Visual Studio's Solution Explorer needs an explicit `<Folder>` item to reliably show a newly created folder. Add to `OutageNotifier/OutageNotifier/OutageNotifier.csproj`, inside the `<Project>` element:

```xml
<ItemGroup>
  <Folder Include="Models\" />
</ItemGroup>
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 3: Configuration options, validator, and dev-template appsettings.json

**Files:**
- Create: `OutageNotifier/OutageNotifier/Configuration/AppOptions.cs`
- Create: `OutageNotifier/OutageNotifier/Configuration/AppOptionsValidator.cs`
- Create: `OutageNotifier/OutageNotifier/appsettings.json`
- Modify: `OutageNotifier/OutageNotifier/OutageNotifier.csproj`

**Interfaces:**
- Produces: `OutageApiOptions`, `DatabaseOptions`, `EmailOptions`, `MatchRule` (all in `OutageNotifier.Configuration`), and `AppOptionsValidator.Validate(OutageApiOptions, DatabaseOptions, EmailOptions, IReadOnlyList<MatchRule>) : IReadOnlyList<string>` — consumed by `Program.cs` (Task 9) and by the matcher/store/api-client/email tasks below.

- [ ] **Step 1: Write the options classes**

```csharp
namespace OutageNotifier.Configuration;

public sealed class OutageApiOptions
{
    public const string SectionName = "OutageApi";

    public string Endpoint { get; set; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = new();
    public string Subject { get; set; } = "EVN Outage Notification";
}

public sealed class MatchRule
{
    public string? NasMesto { get; set; }
    public string? Adresa { get; set; }
}
```

Save as `OutageNotifier/OutageNotifier/Configuration/AppOptions.cs`.

- [ ] **Step 2: Write the validator**

```csharp
namespace OutageNotifier.Configuration;

public static class AppOptionsValidator
{
    public static IReadOnlyList<string> Validate(
        OutageApiOptions api,
        DatabaseOptions database,
        EmailOptions email,
        IReadOnlyList<MatchRule> matchRules)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(api.Endpoint))
            errors.Add("OutageApi:Endpoint is required.");

        if (string.IsNullOrWhiteSpace(database.ConnectionString))
            errors.Add("Database:ConnectionString is required.");

        if (string.IsNullOrWhiteSpace(email.SmtpHost))
            errors.Add("Email:SmtpHost is required.");

        if (string.IsNullOrWhiteSpace(email.From))
            errors.Add("Email:From is required.");

        if (email.To.Count == 0)
            errors.Add("Email:To must contain at least one recipient.");

        if (matchRules.Count == 0)
            errors.Add("MatchRules must contain at least one rule.");

        for (var i = 0; i < matchRules.Count; i++)
        {
            var rule = matchRules[i];
            if (string.IsNullOrWhiteSpace(rule.NasMesto) && string.IsNullOrWhiteSpace(rule.Adresa))
                errors.Add($"MatchRules[{i}] must set at least one of NasMesto or Adresa.");
        }

        return errors;
    }
}
```

- [ ] **Step 3: Write the dev-template `appsettings.json`**

```json
{
  "OutageApi": {
    "Endpoint": "https://example.com/api/outages"
  },
  "Database": {
    "ConnectionString": "Data Source=outages.db"
  },
  "Email": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "UseStartTls": true,
    "Username": "",
    "Password": "",
    "From": "outage-notifier@example.com",
    "To": ["outage-receiver@example.com"],
    "Subject": "EVN Outage Notification"
  },
  "MatchRules": [
    { "NasMesto": "Skopje" }
  ]
}
```

Save as `OutageNotifier/OutageNotifier/appsettings.json`. This is the checked-in dev-time default — safe placeholder values only, never real credentials.

- [ ] **Step 4: Make sure it's copied to the build output, and register the folder for Visual Studio**

Add to `OutageNotifier/OutageNotifier/OutageNotifier.csproj`, inside the `<Project>` element:

```xml
<ItemGroup>
  <None Update="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>

<ItemGroup>
  <Folder Include="Configuration\" />
</ItemGroup>
```

- [ ] **Step 5: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`, and `OutageNotifier/OutageNotifier/bin/Debug/net10.0/appsettings.json` exists after the build.

---

### Task 4: Outage matcher

**Files:**
- Create: `OutageNotifier/OutageNotifier/Services/IOutageMatcher.cs`
- Create: `OutageNotifier/OutageNotifier/Services/OutageMatcher.cs`
- Modify: `OutageNotifier/OutageNotifier/OutageNotifier.csproj`

**Interfaces:**
- Consumes: `OutageNotifier.Models.Outage` (Task 2), `OutageNotifier.Configuration.MatchRule` (Task 3).
- Produces: `IOutageMatcher.IsMatch(Outage outage, IReadOnlyList<MatchRule> rules) : bool` — consumed by `OutageNotifierRunner` (Task 8).

- [ ] **Step 1: Write the interface**

```csharp
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IOutageMatcher
{
    bool IsMatch(Outage outage, IReadOnlyList<MatchRule> rules);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageMatcher : IOutageMatcher
{
    public bool IsMatch(Outage outage, IReadOnlyList<MatchRule> rules)
    {
        foreach (var rule in rules)
        {
            if (RuleMatches(outage, rule))
                return true;
        }

        return false;
    }

    private static bool RuleMatches(Outage outage, MatchRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.NasMesto) && !Contains(outage.NasMesto, rule.NasMesto))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Adresa) && !Contains(outage.Adresa, rule.Adresa))
            return false;

        return true;
    }

    private static bool Contains(string? haystack, string needle)
    {
        return haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Register the folder in the csproj so it shows up in Visual Studio**

Add to `OutageNotifier/OutageNotifier/OutageNotifier.csproj`, inside the `<Project>` element:

```xml
<ItemGroup>
  <Folder Include="Services\" />
</ItemGroup>
```

- [ ] **Step 4: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 5: SQLite notified-outage store

**Files:**
- Create: `OutageNotifier/OutageNotifier/Services/INotifiedOutageStore.cs`
- Create: `OutageNotifier/OutageNotifier/Services/SqliteNotifiedOutageStore.cs`

**Interfaces:**
- Consumes: `OutageNotifier.Configuration.DatabaseOptions` (Task 3).
- Produces: `INotifiedOutageStore` with `EnsureSchemaAsync(CancellationToken) : Task`, `GetNotifiedIdsAsync(CancellationToken) : Task<HashSet<string>>`, `MarkNotifiedAsync(IReadOnlyCollection<string> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken) : Task` - consumed by `OutageNotifierRunner` (Task 8).

- [ ] **Step 1: Write the interface**

```csharp
namespace OutageNotifier.Services;

public interface INotifiedOutageStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task<HashSet<string>> GetNotifiedIdsAsync(CancellationToken cancellationToken);

    Task MarkNotifiedAsync(IReadOnlyCollection<string> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OutageNotifier.Configuration;

namespace OutageNotifier.Services;

public sealed class SqliteNotifiedOutageStore : INotifiedOutageStore
{
    private readonly string _connectionString;

    public SqliteNotifiedOutageStore(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS NotifiedOutages (
                PrekinId TEXT PRIMARY KEY,
                NotifiedAtUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetNotifiedIdsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PrekinId FROM NotifiedOutages;";

        var ids = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task MarkNotifiedAsync(IReadOnlyCollection<string> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM NotifiedOutages WHERE NotifiedAtUtc < $staleCutoff;";

            var staleCutoffParam = deleteCommand.CreateParameter();
            staleCutoffParam.ParameterName = "$staleCutoff";
            staleCutoffParam.Value = notifiedAtUtc.AddMonths(-1).ToString("O");
            deleteCommand.Parameters.Add(staleCutoffParam);

            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (prekinIds.Count > 0)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO NotifiedOutages (PrekinId, NotifiedAtUtc) VALUES ($prekinId, $notifiedAt);";

            var prekinIdParam = insertCommand.CreateParameter();
            prekinIdParam.ParameterName = "$prekinId";
            insertCommand.Parameters.Add(prekinIdParam);

            var notifiedAtParam = insertCommand.CreateParameter();
            notifiedAtParam.ParameterName = "$notifiedAt";
            notifiedAtParam.Value = notifiedAtUtc.ToString("O");
            insertCommand.Parameters.Add(notifiedAtParam);

            foreach (var id in prekinIds)
            {
                prekinIdParam.Value = id;
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
```

**Post-scaffold addition:** `MarkNotifiedAsync` also deletes rows older than one month (`notifiedAtUtc.AddMonths(-1)`) in the same transaction, so `NotifiedOutages` doesn't grow unbounded. This only runs when `MarkNotifiedAsync` is called, i.e. only on days with new matches — a long stretch with zero new outages means stale rows persist past one month until the next actual notification.

- [ ] **Step 3: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 6: Outage API client

**Files:**
- Create: `OutageNotifier/OutageNotifier/Services/IOutageApiClient.cs`
- Create: `OutageNotifier/OutageNotifier/Services/OutageApiClient.cs`

**Interfaces:**
- Consumes: `OutageNotifier.Models.Outage` (Task 2), `OutageNotifier.Configuration.OutageApiOptions` (Task 3).
- Produces: `IOutageApiClient.GetOutagesAsync(CancellationToken) : Task<IReadOnlyList<Outage>>` — consumed by `OutageNotifierRunner` (Task 8).

- [ ] **Step 1: Write the interface**

```csharp
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IOutageApiClient
{
    Task<IReadOnlyList<Outage>> GetOutagesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageApiClient : IOutageApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public OutageApiClient(HttpClient httpClient, IOptions<OutageApiOptions> options)
    {
        _httpClient = httpClient;
        _endpoint = options.Value.Endpoint;
    }

    public async Task<IReadOnlyList<Outage>> GetOutagesAsync(CancellationToken cancellationToken)
    {
        var outages = await _httpClient.GetFromJsonAsync<List<Outage>>(_endpoint, cancellationToken);
        return outages ?? new List<Outage>();
    }
}
```

- [ ] **Step 3: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 7: Email sender

**Files:**
- Create: `OutageNotifier/OutageNotifier/Services/IEmailSender.cs`
- Create: `OutageNotifier/OutageNotifier/Services/MailKitEmailSender.cs`

**Interfaces:**
- Consumes: `OutageNotifier.Models.Outage` (Task 2), `OutageNotifier.Configuration.EmailOptions` (Task 3).
- Produces: `IEmailSender.SendOutageNotificationAsync(IReadOnlyList<Outage> outages, CancellationToken) : Task` — consumed by `OutageNotifierRunner` (Task 8).

- [ ] **Step 1: Write the interface**

```csharp
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IEmailSender
{
    Task SendOutageNotificationAsync(IReadOnlyList<Outage> outages, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the implementation**

```csharp
using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public MailKitEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendOutageNotificationAsync(IReadOnlyList<Outage> outages, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        foreach (var recipient in _options.To)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = _options.Subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Html)
        {
            Text = BuildHtmlBody(outages)
        };

        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string BuildHtmlBody(IReadOnlyList<Outage> outages)
    {
        var sb = new StringBuilder();
        sb.Append("<h2>New Outage Notifications</h2>");
        sb.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\">");
        sb.Append("<tr><th>Type</th><th>Nas. Mesto</th><th>Adresa</th><th>Nap. Nivo</th><th>Pocetok</th><th>Kraj</th></tr>");

        foreach (var outage in outages)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.TipPrekin)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.NasMesto)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.Adresa)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.NapNivo)}</td>");
            sb.Append($"<td>{outage.Pocetok:yyyy-MM-dd HH:mm}</td>");
            sb.Append($"<td>{outage.Kraj:yyyy-MM-dd HH:mm}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 8: Outage notifier runner (orchestrator)

**Files:**
- Create: `OutageNotifier/OutageNotifier/Services/OutageNotifierRunner.cs`

**Interfaces:**
- Consumes: `IOutageApiClient` (Task 6), `INotifiedOutageStore` (Task 5), `IOutageMatcher` (Task 4), `IEmailSender` (Task 7), `IReadOnlyList<MatchRule>` (bound in Task 9), `ILogger<OutageNotifierRunner>`.
- Produces: `OutageNotifierRunner.RunAsync(CancellationToken) : Task` — consumed by `Program.cs` (Task 9).

- [ ] **Step 1: Write the runner**

```csharp
using Microsoft.Extensions.Logging;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageNotifierRunner
{
    private readonly IOutageApiClient _apiClient;
    private readonly INotifiedOutageStore _store;
    private readonly IOutageMatcher _matcher;
    private readonly IEmailSender _emailSender;
    private readonly IReadOnlyList<MatchRule> _matchRules;
    private readonly ILogger<OutageNotifierRunner> _logger;

    public OutageNotifierRunner(
        IOutageApiClient apiClient,
        INotifiedOutageStore store,
        IOutageMatcher matcher,
        IEmailSender emailSender,
        IReadOnlyList<MatchRule> matchRules,
        ILogger<OutageNotifierRunner> logger)
    {
        _apiClient = apiClient;
        _store = store;
        _matcher = matcher;
        _emailSender = emailSender;
        _matchRules = matchRules;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _store.EnsureSchemaAsync(cancellationToken);

        var notifiedIds = await _store.GetNotifiedIdsAsync(cancellationToken);
        var outages = await _apiClient.GetOutagesAsync(cancellationToken);

        var newMatches = outages
            .Where(o => !notifiedIds.Contains(o.PrekinId))
            .Where(o => _matcher.IsMatch(o, _matchRules))
            .ToList();

        if (newMatches.Count == 0)
        {
            _logger.LogInformation("No new matching outages found. {TotalFetched} outages fetched.", outages.Count);
            return;
        }

        _logger.LogInformation("Found {MatchCount} new matching outages. Sending notification email.", newMatches.Count);
        await _emailSender.SendOutageNotificationAsync(newMatches, cancellationToken);

        await _store.MarkNotifiedAsync(
            newMatches.Select(o => o.PrekinId).ToList(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        _logger.LogInformation("Notification sent and {MatchCount} outage(s) recorded.", newMatches.Count);
    }
}
```

- [ ] **Step 2: Verify**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

---

### Task 9: Program.cs wiring

**Files:**
- Modify: `OutageNotifier/OutageNotifier/Program.cs` (replace the entire "Hello, World!" contents)

**Interfaces:**
- Consumes: everything produced in Tasks 2–8.
- Produces: the process exit code (`0`/`1`) that the external cron relies on.

- [ ] **Step 1: Replace `Program.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutageNotifier.Configuration;
using OutageNotifier.Services;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.Configure<OutageApiOptions>(builder.Configuration.GetSection(OutageApiOptions.SectionName));
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

    var matchRules = builder.Configuration.GetSection("MatchRules").Get<List<MatchRule>>() ?? new List<MatchRule>();
    builder.Services.AddSingleton<IReadOnlyList<MatchRule>>(matchRules);

    builder.Services.AddHttpClient<IOutageApiClient, OutageApiClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddSingleton<INotifiedOutageStore, SqliteNotifiedOutageStore>();
    builder.Services.AddSingleton<IOutageMatcher, OutageMatcher>();
    builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
    builder.Services.AddSingleton<OutageNotifierRunner>();

    using var host = builder.Build();

    var apiOptions = host.Services.GetRequiredService<IOptions<OutageApiOptions>>().Value;
    var dbOptions = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var emailOptions = host.Services.GetRequiredService<IOptions<EmailOptions>>().Value;

    var errors = AppOptionsValidator.Validate(apiOptions, dbOptions, emailOptions, matchRules);
    if (errors.Count > 0)
    {
        foreach (var error in errors)
        {
            Log.Error("Configuration error: {Error}", error);
        }

        return 1;
    }

    var runner = host.Services.GetRequiredService<OutageNotifierRunner>();
    await runner.RunAsync(CancellationToken.None);

    Log.Information("Run completed successfully.");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Outage notifier run failed.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build OutageNotifier/OutageNotifier.slnx`
Expected: `Build succeeded.`

- [ ] **Step 3: Verify the failure path (no real API needed)**

Temporarily edit `OutageNotifier/OutageNotifier/bin/Debug/net10.0/appsettings.json` (the built copy — do not edit the source template) and set:

```json
"OutageApi": { "Endpoint": "http://127.0.0.1:59999/outages" }
```

Run, from `OutageNotifier/OutageNotifier/bin/Debug/net10.0`: `dotnet OutageNotifier.dll`
Expected: a structured JSON log line reporting the run failed (connection refused), the process exits non-zero. Confirm with `echo $?` (bash) or `$LASTEXITCODE` (PowerShell) — expect `1`.

Note: `dotnet run --project ...` also works, but its launch profile changes the config lookup's working directory, which can make it look like `appsettings.json` wasn't found even though it was. Running the built `.dll` directly avoids that ambiguity.

Revert the edited built copy afterward (it will be regenerated from the source template on the next build anyway).

---

### Task 10: Docker Compose deployment files

**Files:**
- Create: `docker-compose.yml` (repo root)
- Create: `appsettings.example.json` (repo root)
- Modify: `.gitignore`
- Create: `data/.gitkeep`

**Interfaces:**
- Consumes: `OutageNotifier/OutageNotifier/Dockerfile` (already exists, unmodified).
- Produces: the deployable Compose service referenced in the design's Deployment section.

- [ ] **Step 1: Write `docker-compose.yml`**

The auto-generated `Dockerfile`'s `COPY` instructions (`COPY ["OutageNotifier/OutageNotifier.csproj", "OutageNotifier/"]`) are written relative to the **solution folder** (`OutageNotifier/`), not the repo root — building with the repo root as context fails with `"OutageNotifier/OutageNotifier.csproj": not found`. Point `context` at the solution folder and give `dockerfile` a context-relative path:

```yaml
services:
  outage-notifier:
    image: jankuloskik/evn-outage-notifier
    build:
      context: ./OutageNotifier
      dockerfile: OutageNotifier/Dockerfile
    volumes:
      - ./appsettings.json:/app/appsettings.json:ro
      - ./data:/data
```

- [ ] **Step 2: Write `appsettings.example.json`**

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
    "Username": "your-smtp-username",
    "Password": "your-smtp-password",
    "From": "outage-notifier@example.com",
    "To": ["outage-receiver@example.com"],
    "Subject": "EVN Outage Notification"
  },
  "MatchRules": [
    { "NasMesto": "Skopje", "Adresa": "Vodnjanska" },
    { "NasMesto": "Kumanovo" }
  ]
}
```

This is the template the operator copies to `./appsettings.json` (untracked) and fills in with real values before running `docker compose`.

- [ ] **Step 3: Add gitignore entries for the untracked runtime files**

Append to `.gitignore`:

```gitignore

# Outage Notifier runtime config/data (real appsettings.json holds SMTP credentials)
/appsettings.json
/data/*
!/data/.gitkeep
```

- [ ] **Step 4: Create the data directory placeholder**

Create an empty file at `data/.gitkeep` so the bind-mount target directory exists in a fresh clone.

- [ ] **Step 5: Verify the Docker image still builds**

Run (context is the solution folder, per the note in Step 1 — not the repo root, despite what `CLAUDE.md`'s Docker command currently shows):

`docker build -f OutageNotifier/OutageNotifier/Dockerfile -t outage-notifier ./OutageNotifier`

Expected: image builds successfully (confirms the multi-file project — new `Configuration/`, `Models/`, `Services/` folders — still restores/builds/publishes correctly inside the container).

- [ ] **Step 6: Verify Compose config parses**

Copy `appsettings.example.json` to `appsettings.json` at the repo root (this file is gitignored, so it's safe to create locally with placeholder values), then run:

`docker compose config`

Expected: prints the resolved service definition with no errors (confirms the bind-mount paths and Dockerfile reference are valid).

---

## Self-Review Notes

- **Spec coverage:** every numbered step in the design's "Execution flow" section maps to a task — logging (Task 9), config load/validate (Tasks 3, 9), schema ensure (Task 5), API fetch (Task 6), dedup+match (Tasks 4, 5, 8), no-match short-circuit (Task 8), consolidated HTML email (Task 7), send-then-persist ordering (Task 8), exit codes (Task 9), Docker Compose deployment (Task 10).
- **Type consistency checked:** `Outage.PrekinId` (Task 2) matches the `string` used in `INotifiedOutageStore` (Task 5) and `OutageMatcher`/`OutageNotifierRunner`. `MatchRule` (Task 3) is the same type referenced in `IOutageMatcher` (Task 4) and bound as `IReadOnlyList<MatchRule>` in `Program.cs` (Task 9) and consumed identically in `OutageNotifierRunner` (Task 8).
- **No test project:** intentional per the approved spec; verification throughout is `dotnet build` plus the two manual checks in Tasks 9 and 10.
- **No commits:** every task ends at its last verification step — no git steps are included anywhere in this plan, per explicit instruction.
- **Folder visibility:** Tasks 2, 3, and 4 (the first task to populate each of `Models/`, `Configuration/`, `Services/`) each add a `<Folder Include>` item to `OutageNotifier.csproj` so the new folders render in Visual Studio's Solution Explorer immediately, not just after a reload.
