using TripPlanner.Application;
using TripPlanner.Application.Features.Routes;
using TripPlanner.Application.Features.Validation;
using TripPlanner.Application.Features.Validation.Validators;
using TripPlanner.Application.RouteCalculator;
using TripPlanner.Domain;
using TripPlanner.Infrastructure;
using TripPlanner.Infrastructure.Fakes;

var builder = WebApplication.CreateBuilder(args);

// DI
builder.Services.AddSingleton<IRouteJobRepository, InMemoryRouteJobRepository>();

// Fake accounts service
builder.Services.AddSingleton<IAccountsService, FakeAccountsService>();
// Fake geo service
builder.Services.AddSingleton<IGeoService, FakeGeoService>();
// Fake route calculator
builder.Services.AddSingleton<IRouteCalculator, FakeRouteCalculator>();

builder.Services.AddScoped<CreateRouteHandler>();
builder.Services.AddScoped<GetRouteHandler>();

// Validators
builder.Services.AddScoped<IValidator<CreateRouteCommand>, CreateRouteInputValidator>();
builder.Services.AddScoped<IValidator<CreateRouteCommand>, AccountsValidator>();
builder.Services.AddScoped<IValidator<CreateRouteCommand>, GeoValidator>();

builder.Services.AddScoped<ValidatorChain<CreateRouteCommand>>();

var app = builder.Build();

// POST
app.MapPost("/api/route", async (
    CreateRouteCommand cmd,
    CreateRouteHandler handler,
    CancellationToken ct) =>
{
    try
    {
        var jobId = await handler.Handle(cmd, ct);

        return Results.Accepted($"/api/route/{jobId}", new {jobId});
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = ex.Message
        });
    }
});

// GET
app.MapGet("/api/route/{id}", async (
    Guid id,
    GetRouteHandler handler,
    CancellationToken ct) =>
{
    var result = await handler.Handle(new GetRouteQuery { JobId = id }, ct);
    return Results.Ok(result);
});

app.Run();