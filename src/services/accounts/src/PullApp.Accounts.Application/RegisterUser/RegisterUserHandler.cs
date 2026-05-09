using MediatR;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.RegisterUser;

public class RegisterUserHandler(
	IUserRepository repository,
	IPasswordHasher hasher)
	: IRequestHandler<RegisterUserCommand, int>
{
	public async Task<int> Handle(RegisterUserCommand request, CancellationToken ct)
	{
		if (!await repository.IsEmailUniqueAsync(request.Email, ct))
			throw new Exception("Email already taken");

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

		return user.Id;
	}
}
