using PullApp.Accounts.Api.Middleware;

namespace PullApp.Accounts.Api;

public static class DependencyInjection
{
	public static IServiceCollection AddApi(this IServiceCollection services)
	{
		services.AddOpenApi();
		
		services.AddExceptionHandler<GlobalExceptionHandler>();
		services.AddProblemDetails();

		return services;
	}
}
