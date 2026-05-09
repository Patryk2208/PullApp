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
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(
						Encoding.UTF8.GetBytes(config["Jwt:SecretKey"]!)),
					
					ValidateIssuer   = true, ValidIssuer   = config["Jwt:Issuer"],
					ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
					ValidateLifetime = true,
				};
			});
		services.AddAuthorization();
		
		return services;
	}
}
