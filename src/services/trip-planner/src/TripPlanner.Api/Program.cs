using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using TripPlanner.Api;
using TripPlanner.Api.Checks;
using TripPlanner.Application.Metrics;
using TripPlanner.Api.BackgroundServices;
using TripPlanner.Api.Middleware;
using TripPlanner.Application.Features;
using TripPlanner.Application.Features.Driver;
using TripPlanner.Application.Features.Passenger;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Fakes;
using TripPlanner.Infrastructure.Kafka;
using TripPlanner.Infrastructure.Postgres;
using TripPlanner.Infrastructure.Sse;
using TripPlanner.Infrastructure.Queue;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("/app/config/appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ─── Database ─────────────────────────────────────────────────────────────────

var dbConfig = builder.Configuration.GetSection("TripPlannerDb");
builder.Services.AddOptions<TripPlannerDbOptions>().Bind(dbConfig);

builder.Services.AddSingleton(sp =>
{
    var opts =  sp.GetRequiredService<IOptions<TripPlannerDbOptions>>();
    return new NpgsqlDataSourceBuilder(opts.Value.BuildConnectionString()).Build();
});

// init services
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<ServiceAreaSeeder>();

builder.Services.AddHostedService<MultipleHostedServiceWrapper>(sp =>
    new MultipleHostedServiceWrapper([
        sp.GetRequiredService<DatabaseInitializer>(), sp.GetRequiredService<ServiceAreaSeeder>()
    ]));

builder.Services.AddScoped<DbSession>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());

// ─── Repositories ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<IRouteJobRepository,    PostgresRouteJobRepository>();
builder.Services.AddScoped<IDriverRouteRepository, PostgresDriverRouteRepository>();
builder.Services.AddScoped<IRideRequestRepository, PostgresRideRequestRepository>();
builder.Services.AddScoped<IRideRepository,        PostgresRideRepository>();

// ─── Geo service ──────────────────────────────────────────────────────────────

builder.Services.AddScoped<IGeoService, PostgisGeoService>();

// ─── Service fakes (swap with real impls in prod via config/env) ──────────────

builder.Services.AddSingleton<IAccountsService, FakeAccountsService>();
builder.Services.AddSingleton<IChatService,      FakeChatService>();
builder.Services.AddSingleton<IPaymentsService,  FakePaymentsService>();

// ─── Cache ──────────────────────────────────────────────────────────────────

// builder.Services.AddScoped<IResultRepository, RedisResultRepository>();

// ─── SSE hub ──────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<InMemorySseHub>();
builder.Services.AddSingleton<ISseHub>(sp => sp.GetRequiredService<InMemorySseHub>());

// ─── Queue ─────────────────────────────────────────────────────────
var config = builder.Configuration.GetSection("ComputeQueue");
builder.Services.AddOptions<RabbitMqOptions>().Bind(config);
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    return new ConnectionFactory
    {
        HostName    = options.Host,
        Port        = options.Port,
        UserName    = options.Username,
        Password    = options.Password,
        VirtualHost = options.Vhost,
    };
});
builder.Services.AddSingleton<RabbitConnection>();

builder.Services.AddSingleton<IQueueDtoMapper<ComputeJob>, ComputeJobProtoMapper>();
builder.Services.AddSingleton<IQueueDomainMapper<ComputeJobResult>, ComputeResultProtoMapper>();

builder.Services.AddScoped<IComputePublisher<ComputeJob>, RabbitComputePublisher<ComputeJob>>();

builder.Services.AddSingleton<IHandler<ComputeJobResult>, RouteComputedHandler>();
builder.Services.AddSingleton<RabbitSubscriber<ComputeJobResult>>();
builder.Services.AddHostedService<HostedServiceWrapper>(sp =>
    new HostedServiceWrapper(sp.GetRequiredService<RabbitSubscriber<ComputeJobResult>>()));

// ─── Kafka ────────────────────────────────────────────────────────────────────

var kafkaSection = builder.Configuration.GetSection("EventBus");
builder.Services.AddOptions<KafkaOptions>().Bind(kafkaSection);

builder.Services.AddSingleton<IEventPublisher, EventPublisher>();

// builder.Services.AddSingleton<IHandler<string>, KafkaEventDispatcher>();
// builder.Services.AddSingleton<KafkaConsumerService<string>>();
// builder.Services.AddHostedService<HostedServiceWrapper>(sp =>
//     new HostedServiceWrapper(sp.GetRequiredService<KafkaConsumerService<string>>()));

// ─── Application handlers — Driver ───────────────────────────────────────────

builder.Services.AddScoped<RegisterRouteHandler>();
builder.Services.AddScoped<ModifyRouteHandler>();
builder.Services.AddScoped<CancelRouteHandler>();
builder.Services.AddScoped<ConfirmationHandler>();
builder.Services.AddScoped<DriverArrivedHandler>();
builder.Services.AddScoped<DriverStartRideHandler>();
builder.Services.AddScoped<CompleteRideHandler>();
builder.Services.AddScoped<DriverCancelRideHandler>();

// ─── Application handlers — Passenger ────────────────────────────────────────

builder.Services.AddScoped<CreateRouteRequestHandler>();
builder.Services.AddScoped<SelectRouteHandler>();
builder.Services.AddScoped<CancelRouteRequestHandler>();
builder.Services.AddScoped<PassengerStartRideHandler>();
builder.Services.AddScoped<ConfirmPriceHandler>();
builder.Services.AddScoped<PassengerCancelRideHandler>();

// ─── Observability ────────────────────────────────────────────────────────────

builder.Services.AddSingleton<TripPlannerMetrics>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RabbitHealthCheck>("rabbitmq", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("trip-planner"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(TripPlannerMetrics.MeterName)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.AddOtlpExporter();
});

// ─── OpenAPI ──────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── Build ────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapEndpoints();

app.Run();
