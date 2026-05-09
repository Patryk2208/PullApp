using MediatR;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.LoginUser;

public class LoginUserHandler
	: IRequestHandler<LoginUserCommand, string>
{
	private readonly IUserRepository _userRepository;
	private readonly IPasswordHasher _passwordHasher;
	private readonly IJwtProvider _jwtProvider;

	public LoginUserHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, IJwtProvider jwtProvider)
	{
		_userRepository = userRepository;
		_passwordHasher = passwordHasher;
		_jwtProvider = jwtProvider;
	}

	public async Task<string> Handle(LoginUserCommand request, CancellationToken ct)
	{
		var user = await _userRepository.GetByEmailAsync(request.Email, ct);
        
		if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
		{
			// TODO localization?
			throw new UnauthorizedAccessException("Błędne dane logowania");
		}

		return _jwtProvider.Generate(user);
	}
}