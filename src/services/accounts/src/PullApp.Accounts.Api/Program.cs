using Microsoft.EntityFrameworkCore; // TODO: Violates Clean Architecture (I think). For development only.
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PullApp.Accounts.Application;
using PullApp.Accounts.Application.Metrics;
using PullApp.Accounts.Api;
using PullApp.Accounts.Api.Checks;
using PullApp.Accounts.Infrastructure;
using PullApp.Accounts.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.Services.AddSingleton<AccountsMetrics>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("accounts"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(AccountsMetrics.MeterName)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.AddOtlpExporter();
});

var app = builder.Build();

app.Services.GetRequiredService<AccountsMetrics>();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
    {
        // Schemat już istnieje (baza dev z poprzedniego runu bez historii migracji).
        // Pomijamy, żeby serwis dał się zrestartować bez padania na ponownej migracji.
        app.Logger.LogWarning("Pominięto migrację — schemat już istnieje ({SqlState})", ex.SqlState);
    }
}

app.MapEndpoints();

app.Run();
