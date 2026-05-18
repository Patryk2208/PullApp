using MediatR;
using Microsoft.Extensions.Logging;
using PullApp.Accounts.Application.Metrics;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.LoginUser;

public class LoginUserHandler(
	IUserRepository userRepository,
	IPasswordHasher passwordHasher,
	IJwtProvider jwtProvider,
	AccountsMetrics metrics,
	ILogger<LoginUserHandler> logger)
	: IRequestHandler<LoginUserCommand, string>
{
	public async Task<string> Handle(LoginUserCommand request, CancellationToken ct)
	{
		logger.LogDebug("Login attempt for email={Email}", request.Email);

		var user = await userRepository.GetByEmailAsync(request.Email, ct);

		if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
		{
			metrics.LoginFailed();
			logger.LogWarning("Login failed for email={Email} reason={Reason}",
				request.Email, user is null ? "user_not_found" : "wrong_password");
			throw new UnauthorizedAccessException("Błędne dane logowania");
		}

		var token = jwtProvider.Generate(user);

		metrics.LoginSucceeded();
		logger.LogInformation("Login succeeded userId={UserId} email={Email}", user.Id, request.Email);

		return token;
	}
}
