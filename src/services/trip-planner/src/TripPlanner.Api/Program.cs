using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using TripPlanner.Api;
using TripPlanner.Api.BackgroundServices;
using TripPlanner.Api.Checks;
using TripPlanner.Api.Middleware;
using TripPlanner.Application.Features.Background;
using TripPlanner.Application.Features.Driver;
using TripPlanner.Application.Features.Passenger;
using TripPlanner.Application.Metrics;
using TripPlanner.Application.Repositories;
using TripPlanner.Application.Services;
using TripPlanner.Domain.Compute;
using TripPlanner.Infrastructure.Fakes;
using TripPlanner.Infrastructure.Kafka;
using TripPlanner.Infrastructure.Postgres;
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
    var opts = sp.GetRequiredService<IOptions<TripPlannerDbOptions>>();
    return new NpgsqlDataSourceBuilder(opts.Value.BuildConnectionString()).Build();
});

builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<ServiceAreaSeeder>();
builder.Services.AddHostedService<MultipleHostedServiceWrapper>(sp =>
    new MultipleHostedServiceWrapper([
        sp.GetRequiredService<DatabaseInitializer>(),
        sp.GetRequiredService<ServiceAreaSeeder>(),
    ]));

builder.Services.AddScoped<DbSession>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());

// ─── Repositories ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<IRouteRepository,       PostgresRouteRepository>();
builder.Services.AddScoped<IRideRepository,        PostgresRideRepository>();
builder.Services.AddScoped<IRideRequestRepository, PostgresRideRequestRepository>();
builder.Services.AddScoped<IRouteJobRepository,    PostgresRouteJobRepository>();

// ─── Geo service ──────────────────────────────────────────────────────────────

builder.Services.AddScoped<IGeoService, PostgisGeoService>();

// ─── External service fakes (swap with real gRPC impls when services are ready) ─

builder.Services.AddSingleton<IAccountsService, FakeAccountsService>();
builder.Services.AddSingleton<IChatService,     FakeChatService>();
builder.Services.AddSingleton<IPaymentsService, FakePaymentsService>();

// Kafka publisher is always real — infra is in place.
// Accounts/Chat/Payments remain fakes until those services are implemented.
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();

// ─── RabbitMQ (Route-Calc compute queue) ─────────────────────────────────────

var rabbitConfig = builder.Configuration.GetSection("ComputeQueue");
builder.Services.AddOptions<RabbitMqOptions>().Bind(rabbitConfig);
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

// RouteComputedHandler is scoped (holds DbSession). RabbitSubscriber is singleton and creates
// a fresh DI scope per message — do NOT change this to singleton or the DbSession leaks.
builder.Services.AddScoped<IHandler<ComputeJobResult>, RouteComputedHandler>();
builder.Services.AddSingleton<RabbitSubscriber<ComputeJobResult>>();
builder.Services.AddHostedService<HostedServiceWrapper>(sp =>
    new HostedServiceWrapper(sp.GetRequiredService<RabbitSubscriber<ComputeJobResult>>()));

// ─── Kafka (domain events outbound + driver events inbound) ──────────────────

var kafkaSection = builder.Configuration.GetSection("EventBus");
builder.Services.AddOptions<KafkaOptions>().Bind(kafkaSection);
builder.Services.AddSingleton(kafkaSection.Get<KafkaTopics>() ?? new KafkaTopics());

// KafkaEventDispatcher handles inbound driver-events (disconnect/reconnect from DriverTracker).
// It is singleton because it holds no per-request state.
builder.Services.AddSingleton<KafkaEventDispatcher>();
builder.Services.AddSingleton<KafkaConsumerService<string>>(sp => new KafkaConsumerService<string>(
    sp.GetRequiredService<IOptions<KafkaOptions>>().Value,
    sp.GetRequiredService<KafkaEventDispatcher>(),
    sp.GetRequiredService<ILogger<KafkaConsumerService<string>>>()));
builder.Services.AddHostedService<HostedServiceWrapper>(sp =>
    new HostedServiceWrapper(sp.GetRequiredService<KafkaConsumerService<string>>()));

// ─── Application handlers ─────────────────────────────────────────────────────

// Driver
builder.Services.AddScoped<CreateRouteHandler>();
builder.Services.AddScoped<ActivateRouteHandler>();
builder.Services.AddScoped<DeleteRouteHandler>();
builder.Services.AddScoped<AcceptRideRequestHandler>();
builder.Services.AddScoped<RejectRideRequestHandler>();
builder.Services.AddScoped<DeclareDriverPickupHandler>();
builder.Services.AddScoped<DeclareDriverEndHandler>();

// Passenger
builder.Services.AddScoped<SubmitRouteSearchHandler>();
builder.Services.AddScoped<CreateRideRequestHandler>();
builder.Services.AddScoped<CancelRideHandler>();
builder.Services.AddScoped<DeclarePassengerPickupHandler>();
builder.Services.AddScoped<DeclarePassengerEndHandler>();

// ─── Observability ────────────────────────────────────────────────────────────

builder.Services.AddSingleton<TripPlannerMetrics>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RabbitHealthCheck>("rabbitmq",   tags: ["ready"])
    .AddCheck<KafkaHealthCheck>("kafka",        tags: ["ready"]);

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

app.MapOpenApi();

app.MapEndpoints();

app.Run();
