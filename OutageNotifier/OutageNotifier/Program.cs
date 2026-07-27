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
