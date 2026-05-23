using FluentValidation;

namespace PullApp.Accounts.Application.LoginUser;

public class LoginUserValidator
	: AbstractValidator<LoginUserCommand>
{
	public LoginUserValidator()
	{
		RuleFor(x => x.Email).NotEmpty().EmailAddress();
		RuleFor(x => x.Password).NotEmpty();
	}
}
