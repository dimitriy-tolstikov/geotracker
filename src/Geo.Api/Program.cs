using Geo.Api.Abstractions;
using Geo.Api.Hubs;
using Geo.Api.Services.Ingest;
using Geo.Api.Services.QueryServices;
using Geo.Api.Services.Simulation;
using Geo.Api.Services.ZoneEvents;
using Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddDbContext<GeoDbContext>(opt =>
    opt.UseNpgsql(
            builder.Configuration.GetConnectionString("Default"),
            npgsql => npgsql.UseNetTopologySuite())
        .UseSnakeCaseNamingConvention());

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(opt => opt.SuppressMapClientErrors = false);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));

// --- Модули (реализации — в Services/*) ---
builder.Services.AddScoped<ITrackQueryService, TrackQueryService>();
builder.Services.AddScoped<IBulkIngestService, BulkIngestService>();
builder.Services.AddScoped<IZoneEventService, ZoneEventService>();

if (builder.Configuration.GetValue("Simulator:Enabled", true))
    builder.Services.AddHostedService<TrackSimulatorService>();

var app = builder.Build();

// Авто-применение миграций с retry (БД в compose может стартовать дольше API)
await ApplyMigrationsWithRetryAsync(app);

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<TrackingHub>("/hubs/tracking");

app.Run();

static async Task ApplyMigrationsWithRetryAsync(WebApplication app)
{
    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
            await db.Database.MigrateAsync();
            app.Logger.LogInformation("Миграции применены");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(ex, "БД недоступна (попытка {Attempt}/{Max}), повтор через 3с", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

// Для интеграционных тестов (WebApplicationFactory)
public partial class Program;
