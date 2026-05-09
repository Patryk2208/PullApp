using Microsoft.EntityFrameworkCore; // TODO: Violates Clean Architecture (I think). For development only.
using PullApp.Accounts.Application;
using PullApp.Accounts.Api;
using PullApp.Accounts.Infrastructure;
using PullApp.Accounts.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

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
