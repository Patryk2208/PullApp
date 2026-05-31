using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateLifetime         = true,
        };
    });

builder.Services.AddAuthorization(o =>
    o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<GatewayMetrics>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("gateway"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(GatewayMetrics.MeterName)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.AddOtlpExporter();
});

var app = builder.Build();

Task WriteHealthJson(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var body = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString().ToLowerInvariant(),
        checks = report.Entries.ToDictionary(
            e => e.Key,
            e => (object)new { status = e.Value.Status.ToString().ToLowerInvariant(), description = e.Value.Description })
    });
    return ctx.Response.WriteAsync(body);
}

app.MapHealthChecks("/health",       new HealthCheckOptions { ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthJson });

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var userId = ctx.User.FindFirst("sub")?.Value;
        var role   = ctx.User.FindFirst("role")?.Value;
        if (userId is not null) ctx.Request.Headers["X-User-Id"]   = userId;
        if (role   is not null) ctx.Request.Headers["X-User-Role"] = role;
    }
    await next();
});

var gatewayMetrics = app.Services.GetRequiredService<GatewayMetrics>();
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    var proxyFeature = ctx.Features.Get<IReverseProxyFeature>();
    if (proxyFeature is not null)
    {
        var service = proxyFeature.Route.Config.ClusterId ?? "unknown";
        gatewayMetrics.RecordRequest(service, ctx.Request.Method, ctx.Response.StatusCode, sw.Elapsed.TotalSeconds);
    }
});

app.MapReverseProxy();
app.Run();
