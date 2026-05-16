using Npgsql;
using TripPlanner.Api;
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

// ─── Database ─────────────────────────────────────────────────────────────────

var dbOptions = builder.Configuration
    .GetSection("Database")
    .Get<TripPlannerDbOptions>() ?? new TripPlannerDbOptions();

var dataSource = new NpgsqlDataSourceBuilder(dbOptions.ConnectionString).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddHostedService<DatabaseInitializerService>();
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

// ─── SSE hub ──────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<InMemorySseHub>();
builder.Services.AddSingleton<ISseHub>(sp => sp.GetRequiredService<InMemorySseHub>());

builder.Services.AddSingleton<FakeComputeQueue>();
builder.Services.AddScoped<RouteComputedHandler>();
builder.Services.AddHostedService<FakeComputeWorker>();

// ─── Queue ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ISubscriber, DatabaseInitializer>();
builder.Services.AddSingleton<ISubscriber, ServiceAreaSeeder>();
builder.Services.AddSingleton<ISubscriber, FakeSubscriber<ComputeJobResult>>();

builder.Services.AddSingleton<
    IComputePublisher<ComputeJob>,
    FakeComputePublisher<ComputeJob>>();

var config = builder.Configuration.GetSection("Rabbit");
builder.Services.AddScoped<RabbitComputePublisher<ComputeJob>>();
// todo add mappers

// ─── Kafka ────────────────────────────────────────────────────────────────────

var kafkaSection = builder.Configuration.GetSection("Kafka");
if (kafkaSection.Exists())
{
    var kafkaOptions = kafkaSection.Get<KafkaOptions>()!;
    builder.Services.AddSingleton(kafkaOptions);
    builder.Services.AddSingleton<IEventPublisher, EventPublisher>();
    builder.Services.AddSingleton<IHandler<string>, KafkaEventDispatcher>();
    builder.Services.AddSingleton<KafkaConsumerService<string>>();
    builder.Services.AddHostedService<KafkaSubscriberService>();
}
else
{
    builder.Services.AddSingleton<IEventPublisher, FakeEventPublisher>();
}

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

// ─── OpenAPI ──────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// ─── Build ────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapEndpoints();

app.Run();
