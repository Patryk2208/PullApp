using FluentValidation;
using Microsoft.EntityFrameworkCore; // TODO: Violates Clean Architecture (I think). For development only.
using PullApp.Accounts.Application;
using PullApp.Accounts.Api;
using PullApp.Accounts.Api.Middleware;
using PullApp.Accounts.Domain;
using PullApp.Accounts.Infrastructure.Persistence;
using PullApp.Accounts.Infrastructure.Persistence.Repositories;
using PullApp.Accounts.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AccountsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);

// Szuka Command i Handlerów w warstwie Application.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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
