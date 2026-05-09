using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PullApp.Accounts.Application;
using PullApp.Accounts.Domain;
using PullApp.Accounts.Infrastructure.Authentication;
using PullApp.Accounts.Infrastructure.Persistence;
using PullApp.Accounts.Infrastructure.Persistence.Repositories;
using PullApp.Accounts.Infrastructure.Security;

namespace PullApp.Accounts.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services, 
		IConfiguration config)
	{
		services.AddDbContext<AccountsDbContext>(options =>
			options.UseNpgsql(config.GetConnectionString("DefaultConnection")));
		
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddSingleton<IPasswordHasher, PasswordHasher>();

		services.AddSingleton<IJwtProvider, JwtProvider>();
		
		return services;
	}
}
