using InternetUptimeTracker.Dashboard;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "IUT_");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "InternetUptimeTrackerDashboard";
});

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.Configure<DataOptions>(builder.Configuration.GetSection("Data"));
builder.Services.AddSingleton<DataPaths>();
builder.Services.AddSingleton<StatsBuilder>();
builder.Services.AddSingleton<StatusProbeService>();

var dashboardOptions = builder.Configuration
    .GetSection("Dashboard")
    .Get<DashboardOptions>() ?? new DashboardOptions();

builder.WebHost.UseUrls(dashboardOptions.Url);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = context.Request.Path
        .StartsWithSegments("/vendor", StringComparison.OrdinalIgnoreCase)
        ? "public, max-age=604800"
        : "no-cache, no-store, must-revalidate";

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
}));

app.MapGet("/api/status", async (StatusProbeService probe, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Json(await probe.GetCurrentStatusAsync(cancellationToken));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/data", (HttpRequest request, DataPaths paths, StatsBuilder stats) =>
{
    try
    {
        var source = CsvOutageReader.Read(paths.CsvPath);
        var dashboard = stats.Build(
            source.Outages,
            source,
            request.Query["range"].ToString(),
            request.Query["scale"].ToString());

        return Results.Json(dashboard);
    }
    catch (Exception ex)
    {
        return Results.Json(
            new
            {
                error = "Could not build dashboard data",
                details = ex.Message
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapFallback("/api/{**rest}", () =>
    Results.Json(
        new { error = "Unknown API route" },
        statusCode: StatusCodes.Status404NotFound));

app.Run();
