using InternetUptimeTracker;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "IUT_");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "InternetUptimeTracker";
});

builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection("Tracker"));
builder.Services.AddSingleton<TrackerPaths>();
builder.Services.AddSingleton<DataMigration>();
builder.Services.AddSingleton<CsvLogger>();
builder.Services.AddSingleton<ConnectionStatusManager>();
builder.Services.AddSingleton<Pinger>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Migrate before the worker (and its CsvLogger) creates a fresh header-only
// log in the new data directory.
host.Services.GetRequiredService<DataMigration>().Run();

host.Run();
