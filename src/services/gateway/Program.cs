using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);
IdentityModelEventSource.ShowPII = true; // apparently violates RODO, development only

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
	    var securityKey = new SymmetricSecurityKey(
		    Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!));
	    securityKey.KeyId = "pullapp-key";
	    
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = securityKey,
            ValidateLifetime         = true,
            ValidateSignatureLast = false
        };
    });

builder.Services.AddAuthorization(o =>
    o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS — the web frontend runs on a different origin/port and the browser blocks
// cross-origin fetch / XHR / EventSource (incl. the SSE stream) unless the gateway
// echoes Access-Control-* headers. Origins come from config (Cors:AllowedOrigins)
// with local-dev defaults. AllowCredentials so a direct EventSource(withCredentials)
// or cookie-based auth also works.
const string CorsPolicy = "frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "http://localhost:3000", "http://localhost:4000", "http://localhost:5000",
        "http://127.0.0.1:3000", "http://127.0.0.1:4000", "http://127.0.0.1:5000",
    };
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

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

// CORS before auth so preflight OPTIONS (which carry no token) are answered and
// not rejected by the default RequireAuthenticatedUser policy.
app.UseCors(CorsPolicy);

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
