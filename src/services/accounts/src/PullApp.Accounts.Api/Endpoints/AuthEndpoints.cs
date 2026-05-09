using MediatR;
using PullApp.Accounts.Application.LoginUser;
using PullApp.Accounts.Application.RegisterUser;

namespace PullApp.Accounts.Api.Endpoints;

public class AuthEndpoints : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("api/auth");

		group.MapPost("register", Register);
		group.MapPost("login", Login);
	}

	private static async Task<IResult> Register(
		RegisterUserCommand command, 
		ISender sender, 
		CancellationToken ct)
	{
		var userId = await sender.Send(command, ct);
		return Results.Created($"/api/users/{userId}", userId);
	}

	private static async Task<IResult> Login(
		LoginUserCommand command, 
		ISender sender, 
		CancellationToken ct)
	{
		var token = await sender.Send(command, ct);
		return Results.Ok(new { AccessToken = token });
	}
}
