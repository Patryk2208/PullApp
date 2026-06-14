using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PullApp.Accounts.Api.Middleware;

namespace PullApp.Accounts.Api;

public static class DependencyInjection
{
	public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration config)
	{
		services.AddOpenApi();
		
		services.AddExceptionHandler<GlobalExceptionHandler>();
		services.AddProblemDetails();

		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(options =>
			{
				// KeyId musi pasować do `kid` w tokenie (JwtProvider ustawia "pullapp-key"),
				// inaczej resolver klucza po kid zawodzi i walidacja pada (tak jest w gatewayu).
				var securityKey = new SymmetricSecurityKey(
					Encoding.UTF8.GetBytes(config["Jwt:SecretKey"]!));
				securityKey.KeyId = "pullapp-key";

				options.MapInboundClaims = false; // zostaw claimy "sub"/"email"/"role" jak są
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = securityKey,

					ValidateIssuer   = true, ValidIssuer   = config["Jwt:Issuer"],
					ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
					ValidateLifetime = true,
				};
			});
		services.AddAuthorization();
		
		return services;
	}
}
