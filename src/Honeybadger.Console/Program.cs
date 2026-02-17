using Honeybadger.Console;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Data;
using Honeybadger.Data.Sqlite;
using Honeybadger.Data.Repositories;
using Honeybadger.Host.Agents;
using Honeybadger.Host.Ipc;
using Honeybadger.Host.Memory;
using Honeybadger.Host.Scheduling;
using Honeybadger.Host.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

// Serilog — write to file only; console output is handled by Spectre.Console
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/honeybadger.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/honeybadger-debug.log",
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Debug,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

// Configuration — bind full HoneybadgerOptions for group model resolution
builder.Services.Configure<HoneybadgerOptions>(builder.Configuration);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

// Database — SQLite only (use configured connection string)
var dbSection = builder.Configuration.GetSection(DatabaseOptions.SectionName);
builder.Services.Configure<DatabaseOptions>(dbSection);
var dbOptions = dbSection.Get<DatabaseOptions>() ?? new DatabaseOptions();
builder.Services.AddHoneybadgerSqlite(dbOptions.ConnectionString);

// Configuration validation — fail fast with helpful errors
var agentOptsForValidation = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()
    ?? throw new InvalidOperationException($"Missing configuration section: {AgentOptions.SectionName}");
var securityOptsForValidation = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
    ?? throw new InvalidOperationException($"Missing configuration section: {SecurityOptions.SectionName}");

if (agentOptsForValidation.MaxConcurrentAgents < 1)
    throw new InvalidOperationException("Agent:MaxConcurrentAgents must be >= 1");
if (agentOptsForValidation.CopilotCli.Port is < 1 or > 65535)
    throw new InvalidOperationException("Agent:CopilotCli:Port must be 1-65535");
if (string.IsNullOrWhiteSpace(dbOptions.ConnectionString))
    throw new InvalidOperationException("Database:ConnectionString is required");

// Log warnings for common configuration issues
if (!agentOptsForValidation.CopilotCli.AutoStart)
    Log.Information("Copilot CLI auto-start disabled (set Agent:CopilotCli:AutoStart=true to enable)");
if (!File.Exists(securityOptsForValidation.MountAllowlistPath))
    Log.Warning("Mount allowlist not found at {Path}, will use defaults", securityOptsForValidation.MountAllowlistPath);

// Repositories
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<TaskRepository>();
builder.Services.AddScoped<SessionRepository>();
builder.Services.AddScoped<GroupRepository>();

// Scheduling
builder.Services.AddSingleton<CronExpressionEvaluator>();

// Host infrastructure
var repoRoot = Directory.GetCurrentDirectory();
builder.Services.AddSingleton(sp =>
    new HierarchicalMemoryStore(repoRoot, sp.GetRequiredService<ILogger<HierarchicalMemoryStore>>()));
builder.Services.AddSingleton<MountSecurityValidator>();

// Agent runner — in-process only (LocalAgentRunner)
var ipcDir = Path.Combine(repoRoot, "data", "ipc");
Directory.CreateDirectory(ipcDir);

builder.Services.AddSingleton<IAgentRunner, LocalAgentRunner>();
builder.Services.AddSingleton<IIpcTransport>(sp =>
{
    return new FileBasedIpcTransport(ipcDir, sp.GetRequiredService<ILogger<FileBasedIpcTransport>>());
});
builder.Services.AddSingleton<GroupQueue>(sp =>
{
    var agentOpts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    return new GroupQueue(agentOpts.MaxConcurrentAgents, sp.GetRequiredService<ILogger<GroupQueue>>());
});

// Console chat frontend
builder.Services.AddSingleton<IChatFrontend, ConsoleChat>();

// Hosted services
builder.Services.AddHostedService<CopilotCliService>();
builder.Services.AddHostedService<MessageLoopService>();
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService(sp => new IpcWatcherService(
    sp.GetRequiredService<IIpcTransport>(),
    sp.GetRequiredService<IChatFrontend>(),
    sp.GetRequiredService<CronExpressionEvaluator>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    ipcDir,
    sp.GetRequiredService<ILogger<IpcWatcherService>>()));

var host = builder.Build();

// Ensure data directories exist and DB is migrated
Directory.CreateDirectory(Path.Combine(repoRoot, "groups", "main"));
Directory.CreateDirectory(Path.Combine(repoRoot, "logs"));
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HoneybadgerDbContext>();
    await db.Database.EnsureCreatedAsync();
}

Log.Information("Honeybadger starting...");
await host.RunAsync();
Log.CloseAndFlush();
