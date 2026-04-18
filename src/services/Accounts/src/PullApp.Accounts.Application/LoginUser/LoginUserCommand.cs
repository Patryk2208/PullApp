using MediatR;

namespace PullApp.Accounts.Application.LoginUser;

public record class LoginUserCommand(
	string Email,
	string Password) 
	: IRequest<string>;
	