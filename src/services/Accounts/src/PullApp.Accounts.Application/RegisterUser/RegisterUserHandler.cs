using MediatR;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.RegisterUser;

public class RegisterUserHandler
	: IRequestHandler<RegisterUserCommand, int>
{
	private readonly IUserRepository _repository;
	private readonly IPasswordHasher _hasher;

	public RegisterUserHandler(IUserRepository repository, IPasswordHasher hasher)
	{
		_repository = repository;
		_hasher = hasher;
	}

	public async Task<int> Handle(RegisterUserCommand request, CancellationToken ct)
	{
		if (!await _repository.IsEmailUniqueAsync(request.Email, ct))
			throw new Exception("Email already taken");

		var passwordHash = _hasher.Hash(request.Password);

		var user = new User
		{
			Name         = request.Name,
			Surname      = request.Surname,
			Email        = request.Email,
			PasswordHash = passwordHash,
			BirthDate    = request.BirthDate,
		};

		await _repository.AddAsync(user, ct);

		return user.Id;
	}
}
