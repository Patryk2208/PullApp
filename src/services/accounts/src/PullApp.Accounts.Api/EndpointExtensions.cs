namespace PullApp.Accounts.Api;

public static class EndpointExtensions
{
	public static IApplicationBuilder MapEndpoints(this WebApplication app)
	{
		// Znajdź wszystkie klasy implementujące `IEndpoint` w assembly `Api`.
		var endpointTypes = typeof(Program)
			.Assembly.GetTypes().Where(t =>
				t is { IsClass: true, IsAbstract: false } &&
				typeof(IEndpoint).IsAssignableFrom(t));

		foreach (var type in endpointTypes)
		{
			var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
			endpoint.MapEndpoint(app);
		}

		return app;
	}
}