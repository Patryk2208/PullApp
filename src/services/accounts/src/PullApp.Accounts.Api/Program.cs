using Microsoft.EntityFrameworkCore; // TODO: Violates Clean Architecture (I think). For development only.
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PullApp.Accounts.Application;
using PullApp.Accounts.Application.Metrics;
using PullApp.Accounts.Api;
using PullApp.Accounts.Infrastructure;
using PullApp.Accounts.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.Services.AddSingleton<AccountsMetrics>();

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

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    db.Database.Migrate();
}

app.MapEndpoints();

app.Run();
