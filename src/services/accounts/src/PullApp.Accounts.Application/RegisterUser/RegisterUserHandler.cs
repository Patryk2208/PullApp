using MediatR;
using Microsoft.Extensions.Logging;
using PullApp.Accounts.Application.Metrics;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.RegisterUser;

public class RegisterUserHandler(
	IUserRepository repository,
	IPasswordHasher hasher,
	AccountsMetrics metrics,
	ILogger<RegisterUserHandler> logger)
	: IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
	public async Task<RegisterUserResponse> Handle(RegisterUserCommand request, CancellationToken ct)
	{
		logger.LogDebug("RegisterUser: email={Email}", request.Email);

		if (!await repository.IsEmailUniqueAsync(request.Email, ct))
		{
			logger.LogWarning("RegisterUser: email already taken email={Email}", request.Email);
			throw new Exception("Email already taken");
		}

		var passwordHash = hasher.Hash(request.Password);

		var user = new User
		{
			Name         = request.Name,
			Surname      = request.Surname,
			Email        = request.Email,
			PasswordHash = passwordHash,
			BirthDate    = request.BirthDate,
		};

		await repository.AddAsync(user, ct);

		metrics.UserRegistered();
		logger.LogInformation("User registered userId={UserId} email={Email}", user.Id, request.Email);

		return new RegisterUserResponse(UserId: user.Id);
	}
}
