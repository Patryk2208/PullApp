using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.Configuration;
using Microsoft.IdentityModel.Tokens;
using PullApp.Accounts.Application;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Infrastructure.Authentication;

internal sealed class JwtProvider : IJwtProvider
{
	private readonly IConfiguration _config;

	public JwtProvider(IConfiguration config)
	{
		var secretKey = config["Jwt:SecretKey"];
		if (secretKey is null || secretKey.Length < 32) 
			throw new InvalidConfigurationException(
				"Jwt:SecretKey must be at least 32 characters long!");
		
		_config = config;
	}

	public string Generate(User user)
	{
		var claims = new Claim[] {
			new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
			new(JwtRegisteredClaimNames.Email, user.Email)
		};

		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
		var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
		
		var token = new JwtSecurityToken(
			issuer: _config["Jwt:Issuer"],
			audience: _config["Jwt:Audience"],
			claims: claims,
			expires: DateTime.UtcNow.AddHours(1),
			signingCredentials: creds
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}
