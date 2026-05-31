using System.Security.Claims;
using MediatR;
using PullApp.Accounts.Application.GetUser;

namespace PullApp.Accounts.Api.Endpoints;

public class UserEndpoints : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("api/users");
		group.MapGet("me", Me).RequireAuthorization();
	}
	
	private static async Task<IResult> Me(ClaimsPrincipal claimsPrincipal, IMediator mediator)
	{
		var email =
			claimsPrincipal.FindFirst(ClaimTypes.Email)?.Value ?? 
			claimsPrincipal.FindFirst("email")?.Value;

		if (string.IsNullOrEmpty(email))
			return Results.Unauthorized();

		var result = await mediator.Send(new GetUser.Query(email));
		return Results.Ok(result);
	}
}
